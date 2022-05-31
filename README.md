# HallPass
Client-side rate limiter library for .NET to help client-side developers respect the rate limits of the APIs that they consume.

## Installation
```
// nuget package manager
Install-Package HallPass

// dotnet cli
dotnet add package HallPass
```

## Usage
Suppose you want to consume an API with the endpoint `https://api.foo.com/users`, which imposes a rate limit of **10 requests per minute**, implemented via a standard Token Bucket algorithm where you can burst 10 requests every minute.

To respect this rate limit in our application, we should create a single `IHallMonitor` keyed to this endpoint with the correct `IPolicy`:
```
IPolicy policy = Policies.TokenBucket(requestsPerPeriod: 10, periodDuration: TimeSpan.FromMinutes(1));
IHallMonitor usersMonitor = new LocalHallMonitor("https://api.foo.com/users", policy);
```

Then, anywhere in our application that needs to rate limit this particular API, we can use our `IHallMonitor` like this:
```
var userId = 13;
FooResult result = await usersMonitor.Request<FooResult>(() => RequestFromFooAsync($"https://api.foo.com/users/{userId}"));
```

### Throttle a Single Call
To throttle a request anywhere in your application, just make sure you're using the same `IHallMonitor` for a given endpoint at each place that you want to share the same rate limit.

```
// somewhere in your configuration
var policy = Policies.TokenBucket(requestsPerPeriod: 10, periodDuration: TimeSpan.FromMinutes(1));
var hallMonitor = new LocalHallMonitor("some/unique/uri/with/a/rate/limit", policy);

// somewhere in your application
FooResult result = await hallMonitor.Request<FooResult>(() => RequestFromExternalApiAsync());
```

### Throttle a Bunch of Calls in a Loop
HallPass works for synchronous loops (within async methods) by simply awaiting until it has permission to proceed based on the provided `IPolicy`.

```
var policy = Policies.TokenBucket(requestsPerPeriod: 10, periodDuration: TimeSpan.FromMinutes(1));
var hallMonitor = new LocalHallMonitor("some/unique/uri/with/a/rate/limit", policy);

var results = new List<FooResult>();

var idsToGet = Enumerable.Range(1, 500);
foreach (var id in idsToGet)
{
    FooResult result = await hallMonitor.Request<FooResult>(() => RequestFromExternalApiAsync(id));
    results.Add(result);
}
```

In this example, the first 10 requests would burst immediately, and then the 11th would be awaited for roughly _1 minute_, at which point it would burst 11-20... at which point it would wait roughly another minute, etc.

### Throttle a Bunch of Calls Concurrently
HallPass is also thread-safe, working as expected for concurrent bunches of requests:

```
var policy = Policies.TokenBucket(requestsPerPeriod: 10, periodDuration: TimeSpan.FromMinutes(1));
var hallMonitor = new LocalHallMonitor("some/unique/uri/with/a/rate/limit", policy);

var results = new List<FooResult>();
var tasks = Enumerable
    .Range(1, 500)
    .Select(id => Task.Run(async () =>
    {
        var result = await hallMonitor.Request<FooResult>(() => RequestFromExternalApiAsync(id));
        results.Add(result);
    }))
    .ToList();

await Task.WhenAll(tasks);
```

### COMING SOON: Throttle a Bunch of Calls Across Distributed Systems
Eventually, HallPass will be able to throttle calls across distributed systems. If you have multiple instances of an application running at once, but need to respect a single external API rate limit, or if you have multiple different applications running but still need to respect a single external API rate limit between all instances and applications, you'd be able to do so like this:

```
// in the configuration of each of your client instances/applications
var policy = Policies.TokenBucket(requestsPerPeriod: 10, periodDuration: TimeSpan.FromMinutes(1));
var hallMonitor = new DistributedHallMonitor("some/unique/uri/with/a/rate/limit", policy, "your-hallpass-app-key", "your-hallpass-app-secret");

// in your instances/applications
FooResult result = await hallMonitor.Request<FooResult>(() => RequestFromExternalApiAsync());
```

HallPass will take care of registering individual instances, "fairly" dolling out permissions, and tracking the global rate limit for your account/app and its usage on our servers.

HallPass will never know what endpoints you're calling, because the actual API call is still handled locally within each application. All that HallPass receives is an encrypted unique ID representing each `IHallMonitor`'s unique key, and the policy used for that key.