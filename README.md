# HallPass - PRE-RELEASE

Client-side rate limiter library for .NET to help client-side developers respect the rate limits of the APIs that they consume.

## Installation

### Nuget Package Manager

```
Install-Package HallPass
```

### dotnet CLI

```
dotnet add package HallPass
```

## Usage

### Configuration

#### Using a HallPass-specific HttpClient

```
using HallPass;

...

// Register HallPass and hook into IHttpClientFactory.CreateHallPassClient() extension method
builder.Services.AddHallPass(config =>
{
    // throttle all requests matching a uri pattern
    config.UseLeakyBucket(
        uriPattern: "api.foo.com/users",
        rate: 100,
        frequency: TimeSpan.FromMinutes(15),
        capacity: 100);

    // can also use a Func<HttpRequestMessage, bool> to resolve whether to throttle or not
    config.UseLeakyBucket(
        httpRequestMessage => httpRequestMessage.RequestUri.ToString().Contains("api.foo.com/posts"),
        1000,
        TimeSpan.FromMinutes(1),
        1000);
});
```

#### Using the default HttpClient

```
using HallPass;

...

// Register HallPass and hook into IHttpClientFactory.CreateClient()
builder.Services.AddHallPass(config =>
{
    // use the default HttpClient, not just the HallPass specific named client
    config.UseDefaultHttpClient = true;

    // throttle all requests matching a uri pattern
    config.UseLeakyBucket(
        uriPattern: "api.foo.com/users",
        rate: 100,
        frequency: TimeSpan.FromMinutes(15),
        capacity: 100);

    // can also use a Func<HttpRequestMessage, bool> to resolve whether to throttle or not
    config.UseLeakyBucket(
        httpRequestMessage => httpRequestMessage.RequestUri.ToString().Contains("api.foo.com/posts"),
        1000,
        TimeSpan.FromMinutes(1),
        1000);
});
```

### Usage - Throttle a single call

It's highly recommended to use the built-in .NET `IHttpClientFactory` to obtain instances of `HttpClient` when using HallPass. Under the hood, HallPass adds an additional `DelegateHandler` to the named HallPass `HttpClient`, which handles the throttling action per configured endpoint.

```
using HallPass;

...

class MyService
{
    private readonly IHttpClientFactory _httpClientFactory;

    public MyService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<FooUser> GetFooUserAsync(string userId, CancellationToken token = default)
    {
        // HallPass extension method
        HttpClient httpClient = _httpClientFactory.CreateHallPassClient();

        ...
        // block to keep rate within 100 / 15 minutes
        await httpClient.GetAsync($"https://api.foo.com/users/{userId}", token);
    }
}
```

### Usage - Throttle a bunch of calls in a loop

HallPass works for synchronous loops (within async methods) by simply awaiting until it has permission to proceed based on the configuration.

```
var userIds = Enumerable.Range(1, 500);
foreach (var userId in userIds)
{
    // block to keep rate within 100 / 15 minutes
    await httpClient.GetAsync($"https://api.foo.com/users/{userId}", token);
}
```

### Usage - Throttle a bunch of calls concurrently

HallPass is also thread-safe, working as expected for concurrent bunches of requests. In this case, it's generally better to allow `IHttpClientFactory` to manage instances of `HttpClient` per `Task`.

```
var tasks = Enumerable
    .Range(1, 500)
    .Select(userId => Task.Run(async () =>
    {
        // usually it's better to get a client per Task, allowing IHttpClientFactory to manage the concurrency complexity
        var httpClient = _httpClientFactory.CreateHallPassClient();

        // block to keep rate within 100 / 15 minutes
        await httpClient.GetAsync($"https://api.foo.com/users/{userId}", token);
    }))
    .ToList();

await Task.WhenAll(tasks);
```

## COMING SOON

- Throttle a Bunch of Calls Across Distributed Systems - (demo is available at [hallpass.dev](https://hallpass.dev))
- More Bucket Types (_Fixed Window_, _Sliding Log_, _Sliding Window_, _Query Cost_, _Concurrency Limit_)

### DEMO: Throttle a Bunch of Calls Across Distributed Systems

HallPass can now throttle calls across distributed systems IN NON-PRODUCTION ENVIRONMENTS. If you have multiple instances of an application running at once, but need to respect a single external API rate limit, or if you have multiple different applications running but still need to respect a single external API rate limit between all instances and applications, you're able to do so with minimal code changes.

#### Configuration

```
builder.Services.AddHallPass(config =>
{
    // remote buckets, for coordinating clusters of services
    config
        .UseLeakyBucket("api.bar.com/statuses", 50, TimeSpan.FromMinutes(1), 50)

        // client id and secret provided when registering an app in your HallPass dashboard
        // 'key' is essential, acting as the shared token used to group/coordinate multiple instances
        .ForMultipleInstances("my-client-id", "my-client-secret", key: "api.bar.com/statuses");
});
```

#### Usage

```
/* this line will wait, if necessary, so the frequency remains within 50 requests
   per 1 minute ACROSS ALL DISTRIBUTED INSTANCES for the given HallPass client_id,
   as defined in the configuration */
await httpClient.GetAsync($"https://api.bar.com/statuses");
```

#### Distributed Throttling Notes

This will be a paid service, but right now demo clients are available with very limited lifetimes, and the HallPass API itself will be rate-limited (with basic throttling and retries handled via the SDK code). We're still finalizing the pricing model, but hope to have a free tier available to demo soon!

HallPass will take care of registering individual instances, "fairly" dolling out permissions, and tracking the global rate limit for your account/app and its usage on our servers.

HallPass will never know what endpoints you're calling, because the actual API call is still handled locally within each application. All that HallPass receives is an encrypted unique ID representing each scoped throttle group, and the bucket type used for that key.

API calls to the HallPass service will be limited. Broadly, the SDK will request and store bunches of _HallPasses_ that can be used locally. When the local cache runs out, it will reach out to the API again for a refill.

### COMING SOON: More Bucket Types

Please let us know which bucket types would be valuable to have. _Query Cost_ will likely either need significant configuration on the side of the user, or will need to be tailored to individual APIs.
