using System;
using System.Collections.Generic;
using System.Linq;

namespace HallPass.Helpers
{
    /// <summary>
    /// A thread-safe generic sorted stack, prioritizing fast front removals and insertions of multiple items less than O(n)
    /// </summary>
    /// <typeparam name="TItem"></typeparam>
    internal sealed class ConcurrentSortedStack<TItem>
    {
        private readonly LinkedList<TItem> _stack = new();
        private readonly IComparer<TItem> _comparer;

        /// <summary>
        /// Create a new ConcurrentSortedStack with an optional IComparer to use for sorting.
        /// </summary>
        /// <param name="comparer">If not provided, the default comparer will be used instead</param>
        public ConcurrentSortedStack(IComparer<TItem> comparer = null)
        {
            _comparer = comparer ?? Comparer<TItem>.Default;
        }

        /// <summary>
        /// If any values exist, it removes the first one and returns true, providing the value as an out parameter
        /// </summary>
        /// <param name="item">The item removed if true, otherwise the default value</param>
        /// <returns>True if an item was successfully removed, otherwise false.</returns>
        public bool TryPop(out TItem item)
        {
            lock (_stack)
            {
                if (_stack.Count == 0)
                {
                    item = default;
                    return false;
                }
                else
                {
                    item = _stack.First.Value;
                    _stack.RemoveFirst();
                    return true;
                }
            }
        }

        /// <summary>
        /// Adds a single item into its properly sorted location among any existing items.
        /// </summary>
        /// <param name="item">The item to add</param>
        public void Add(TItem item)
        {
            lock (_stack)
            {
                // if there's nothing in the stack, add the item immediately
                if (_stack.Count == 0)
                {
                    _stack.AddFirst(item);
                }

                // if the new item is greater than the last item in the stack, add it immediately to the end
                else if (_comparer.Compare(item, _stack.Last.Value) >= 0)
                {
                    _stack.AddLast(item);
                }

                // otherwise, cycle through each inner item from start to end-1 to find where it should go
                else
                {
                    var current = _stack.First;

                    do
                    {
                        if (_comparer.Compare(item, current.Value) <= 0)
                        {
                            _stack.AddBefore(current, item);
                            return;
                        }

                        current = current.Next;

                        // end with the N-1 item, since we already checked the last
                    } while (current.Next != null);

                    // since we already checked the last, and made it through the loop, we know it goes right before the last
                    _stack.AddBefore(_stack.Last, item);
                }
            }
        }

        /// <summary>
        /// Adds a group of items into their properly sorted locations among any existing items.
        /// </summary>
        /// <param name="items">The items to add</param>
        public void Add(IEnumerable<TItem> items)
        {
            if (items is null || !items.Any())
                return;

            // sort them now so insertions of 2+ items will be faster than O(n)
            var sortedItems = items.ToArray();
            Array.Sort(sortedItems, _comparer);

            lock (_stack)
            {
                // if the stack is empty, add each item in order immediately
                if (_stack.Count == 0)
                {
                    foreach (var item in sortedItems)
                    {
                        _stack.AddLast(item);
                    }
                }

                // if the first new item is greater than the last existing item in the stack, then add each new item in order immediately following the existing last item
                else if (_comparer.Compare(sortedItems[0], _stack.Last.Value) >= 0)
                {
                    foreach (var item in sortedItems)
                    {
                        _stack.AddLast(item);
                    }
                }

                // otherwise, start from the beginning, remembering the last position checked for each new item
                else
                {
                    var current = _stack.First;
                    foreach (var item in sortedItems)
                    {
                        while (true)
                        {
                            // current will eventually be null if we make it all the way through the loop
                            // for subsequent items, we'll also want to immediately add them last
                            if (current is null)
                            {
                                _stack.AddLast(item);
                                break;
                            }

                            else if (_comparer.Compare(item, current.Value) <= 0)
                            {
                                _stack.AddBefore(current, item);
                                break;
                            }

                            current = current.Next;
                        }
                    }
                }
            }
        }
    }
}
