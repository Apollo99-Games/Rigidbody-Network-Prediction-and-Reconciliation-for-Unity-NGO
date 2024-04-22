using System;
using System.Collections.Generic;
using Unity.VisualScripting;

/// <summary>
/// A buffer built for the objects using the IPayLoad interface. 
/// Has methods to prevent the buffer from exceeding a specific size. 
/// Sorts the objects according to their tick.
/// </summary>
public class PayLoadBuffer<T> where T : struct, IPayLoad {
    private List<T> buffer;
    private List<T> lastItems;
    private T lastItem;
    private int sizeToMaintain;
    private int sizeThreshold;
    private int maxEatCount;
    private int absoluteMaxSize;
    private bool Shrink;

    /// <summary>
    /// Creates a pay load buffer that tries to keep a certain max size. 
    /// Note these constraints only apply to the EnqueueRedundentItems() and DequeueToMaintain() methods
    /// </summary>
    /// <param name="sizeToMaintain">The max size the buffer should try to keep.</param>
    /// <param name="sizeThreshold">The threshold the buffer is allowed to have.</param>
    /// <param name="maxEatCount">The maximum number of objects the buffer can Dequeue at once.</param>
    /// <param name="absoluteMaxSize">If the buffer exceeds this value it will not except anymore inputs</param>
    public PayLoadBuffer(int sizeToMaintain, int sizeThreshold, int maxEatCount = Int32.MaxValue, int absoluteMaxSize = Int32.MaxValue) {
        buffer = new List<T>();
        lastItems = new List<T>();
        this.sizeToMaintain = sizeToMaintain;
        this.sizeThreshold = sizeThreshold;
        Shrink = false;
        this.maxEatCount = maxEatCount;
        this.absoluteMaxSize = absoluteMaxSize;
    }

    /// <summary>
    /// Enqueues a list into the buffer. 
    /// Ensures there are not duplicate ticks.
    /// The ticks of the objects are calculated by subtracting one from the provided tick. 
    /// </summary>
    /// <param name="items">The list of items you want to add to the buffer</param>
    /// <param name="tick">The game tick of the items</param>
    /// <param name="max">The maximum number of items inserted at this time</param>
    /// <returns>bool - if the the buffer has room to add the object</returns>
    public bool EnqueueRedundentItems(List<T> items, int tick, int max = -1) {
        if (buffer.Count + items.Count > absoluteMaxSize) return false;

        for (int i = 0; i < items.Count; i++)
        {
            // Ensure the objects do not exceed the max number
            if (max != -1 && i + 1 > max) break;

            // Calculate and set the tick
            T item = items[i];
            item.Tick = tick - i;

            // If the 
            if(!ContainsTick(tick - i)) {
                Enqueue(item);
            }
        }
        return true;
    }

    /// <summary>
    /// Enqueues a list into the  and shorts them by tick
    /// </summary>
    /// <param name="item">The item to be want to added to the buffer</param>
    /// <returns>void</returns>
    public void Enqueue(T item) {
        for (int i = buffer.Count - 1; i >= 0; i--)
        {
            if (buffer[i].Tick > item.Tick) {

                buffer.Insert(i + 1, item);
                return;
            }
        }
        buffer.Insert(0, item);
    }

    /// <summary>
    /// Dequeues the buffer in a way in which it tries to ensure the length stays below the max size
    /// </summary>
    /// <returns>List of Dequeued items</returns>
    public List<T> DequeueToMaintain() {
        List<T> BufferToFill = new List<T>();
        lastItems.Clear();

        if (buffer.Count == 0) return BufferToFill;

        if (buffer.Count <= sizeToMaintain) Shrink = false;
        
        // If the buffer size is greater than the allowed threshold
        // Shrink it to the required size.
        // else just Dequeue as normal
        if (buffer.Count > sizeThreshold + sizeToMaintain || Shrink ) {
            Shrink = true;

            int EatCount = buffer.Count - sizeToMaintain;
            if (EatCount > maxEatCount) EatCount = maxEatCount;

            for (int i = 0; i < EatCount; i++)
            {
                T holder = InternalDequeue();
                lastItems.Add(holder);
                BufferToFill.Add(holder);
            }
        }
        else {
            BufferToFill.Add(Dequeue());
        }

        return BufferToFill;
    }

    /// <summary>
    /// Dequeues the buffer. 
    /// </summary>
    /// <returns>the Dequeued item</returns>
    private T InternalDequeue() {
        T item = buffer[buffer.Count - 1];
        buffer.RemoveAt(buffer.Count - 1);
        lastItem = item;
        return item;
    }

    /// <summary>
    /// Dequeues the buffer
    /// </summary>
    /// <returns>the Dequeued item</returns>
    public T Dequeue() {
        InternalDequeue();
        lastItems.Clear();
        lastItems.Add(lastItem);
        return lastItem;
    }

    /// <summary>
    /// Finds if the buffer already contains a tick.
    /// Will return false if the tick is smaller or equal to the last dequeued tick
    /// </summary>
    /// <param name="tick">The tick to find</param>
    /// <returns>boolean - if it contains or doesn't contain the tick</returns>
    public bool ContainsTick(int tick) {
        if (buffer.Count == 0) return false;
        if (!lastItem.Equals(default(T)) && tick <= lastItem.Tick) return true;
        foreach (var item in buffer)
        {
            if (item.Tick == tick) {
                return true;
            }
        }
        return false;
    }

    public int Size() {
        return buffer.Count;
    }

    /// <summary>
    /// Gets the last objects dequeued
    /// </summary>
    /// <returns>the List of objects</return>
    public List<T> GetLastItems() {
        return lastItems;
    }

    /// <summary>
    /// Gets the last object dequeued
    /// </summary>
    /// <returns>the object</return>
    public T GetLastItem() {
        return lastItem;
    }

    /// <summary>
    /// Checks if a object list contains a specifc ID
    /// </summary>
    /// <param name="items">The list to search</param>
    /// <param name="ObjectID">The object ID to search for</param>
    /// <returns>boolean - if the ID was found</returns>
    public static bool ContainsID(List<T> items, int ObjectID) {
        foreach (var item in items)
        {
            if(item.ObjectID == ObjectID) return true;
        }
        return false;
    }

    /// <summary>
    /// Finds all the objects with a specific ID
    /// </summary>
    /// <param name="items">The list to search</param>
    /// <param name="ObjectID">The object ID to search for</param>
    /// <returns>the List of objects</returns>
    public static List<T> FindObjectItems(List<T> items, int ObjectID) {
        List<T> BufferToFill = new List<T>();
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].ObjectID == ObjectID) {
                BufferToFill.Add(items[i]);
            }
        }
        return BufferToFill;
    }

    /// <summary>
    /// Finds the first object with a specific ID in a list
    /// </summary>
    /// <param name="items">The list to search</param>
    /// <param name="ObjectID">The object ID to search for</param>
    /// <returns>the object found or the default value of the object if it is not found</returns>
    public static T FindObjectItem(List<T> items, int ObjectID) {
        foreach (var item in items)
        {
            if (item.ObjectID == ObjectID) {
                return item;
            }
        }
        return default;
    }

    /// <summary>
    /// Compresses a list of objects with the ICompressible interface. Does this is deleting duplicate objects.
    /// The function will still store the number of duplicates so it can be Decompressed later.
    /// This will ignore the tick value
    /// </summary>
    /// <param name="items">The list to compress</param>
    /// <returns>void - Modifies the list passed in</returns>
    // Example Decompressed: Object Value: 1, Object Value: 1, Object Value: 1, Object Value: 2, Object Value: 2
    // Example Compressed: Object Value: 1 (Duplicates: 2), Object Value: 2 (Duplicates: 1)
    public static void Compress<item>(List<item> items) where item: struct, ICompressible {
        if (items.Count < 2) return;

        item LastDiff = items[0];
        LastDiff.Tick = 0;
        int LastDiffIndex = 0;
        int Duplicates = 0;
        for (int i = 1; i < items.Count; i++)
        {
            // Set the tick value to 0 as it doesn't matter
            item holder = items[i];
            holder.Tick = 0;

            // if the last unique object was the same as the current one
            // the current one will be removed and will increment the counter to keep track of the number of duplicates 
            if (LastDiff.Equals(holder)) {
                items.RemoveAt(i);
                i--;
                Duplicates++;
            }
            else {
                // if the current object is different than the last unique object. 
                // Store number of duplicates in the last object
                // Now the current object unique object will become the last object 
                LastDiff.NumberOfCopies = (byte)Duplicates;
                items[LastDiffIndex] = LastDiff;
                Duplicates = 0;

                LastDiff = items[i];
                LastDiff.Tick = 0;
                LastDiffIndex = i;
            }
        }
        LastDiff.NumberOfCopies = (byte)Duplicates;
        items[LastDiffIndex] = LastDiff;
    }

    /// <summary>
    /// Decompress a list of objects with the ICompressible interface. Does this is by adding the duplicate objects in the list.
    /// </summary>
    /// <param name="items">The list to Decompress</param>
    /// <returns>void - Modifies the list passed in</returns>
    public static void Decompress<item>(List<item> items) where item: struct, ICompressible {
        for (int i = 0; i < items.Count; i++)
        {
            // Makes duplicates of an object 
            // the extact number of objects is stored in the GetNumberOfCopies() method.
            for (int indexCopy = 1; indexCopy <= items[i].NumberOfCopies; indexCopy++)
            {
                if (i + indexCopy < items.Count) {
                    items.Insert(i + indexCopy, items[i]);
                }
                else {
                    items.Add(items[i]);
                }
            }
            
            i += items[i].NumberOfCopies;
        }
    }
}