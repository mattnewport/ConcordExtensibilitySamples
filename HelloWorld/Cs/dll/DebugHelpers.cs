using Microsoft.VisualStudio.Debugger;

static class DebugHelpers
{
    internal static T GetOrCreateDataItem<T>(DkmDataContainer container) where T : DkmDataItem, new()
    {
        T item = container.GetDataItem<T>();

        if (item != null)
            return item;

        item = new T();

        container.SetDataItem<T>(DkmDataCreationDisposition.CreateNew, item);

        return item;
    }
}