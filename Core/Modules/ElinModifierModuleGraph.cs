using System;
using System.Collections.Generic;

internal static class ElinModifierModuleGraph
{
    internal static IReadOnlyList<ElinModifierModuleDescriptor> Order(
        IEnumerable<ElinModifierModuleDescriptor> descriptors)
    {
        if (descriptors == null)
            throw new ArgumentNullException(nameof(descriptors));

        var byId = new Dictionary<string, ElinModifierModuleDescriptor>(StringComparer.Ordinal);
        foreach (var descriptor in descriptors)
        {
            if (descriptor == null)
                throw new InvalidOperationException("Module descriptor cannot be null.");
            if (!byId.TryAdd(descriptor.Id, descriptor))
                throw new InvalidOperationException("Duplicate module id: " + descriptor.Id);
        }

        var inDegree = new Dictionary<string, int>(StringComparer.Ordinal);
        var dependents = new Dictionary<string, List<ElinModifierModuleDescriptor>>(StringComparer.Ordinal);
        foreach (var descriptor in byId.Values)
        {
            inDegree[descriptor.Id] = descriptor.Dependencies.Count;
            for (var i = 0; i < descriptor.Dependencies.Count; i++)
            {
                var dependency = descriptor.Dependencies[i];
                if (string.IsNullOrWhiteSpace(dependency) || !byId.ContainsKey(dependency))
                    throw new InvalidOperationException(
                        "Module '" + descriptor.Id + "' has missing dependency '" + dependency + "'.");

                List<ElinModifierModuleDescriptor>? list;
                if (!dependents.TryGetValue(dependency, out list))
                {
                    list = new List<ElinModifierModuleDescriptor>();
                    dependents[dependency] = list;
                }
                list.Add(descriptor);
            }
        }

        var result = new List<ElinModifierModuleDescriptor>(byId.Count);
        var ready = new List<ElinModifierModuleDescriptor>();
        foreach (var descriptor in byId.Values)
        {
            if (inDegree[descriptor.Id] == 0)
                ready.Add(descriptor);
        }
        ready.Sort(CompareDescriptors);

        while (ready.Count > 0)
        {
            var descriptor = ready[0];
            ready.RemoveAt(0);
            result.Add(descriptor);

            List<ElinModifierModuleDescriptor>? next;
            if (!dependents.TryGetValue(descriptor.Id, out next))
                continue;
            next.Sort(CompareDescriptors);
            for (var i = 0; i < next.Count; i++)
            {
                var dependent = next[i];
                var remaining = inDegree[dependent.Id] - 1;
                inDegree[dependent.Id] = remaining;
                if (remaining == 0)
                {
                    ready.Add(dependent);
                    ready.Sort(CompareDescriptors);
                }
            }
        }

        if (result.Count != byId.Count)
            throw new InvalidOperationException("Circular module dependency detected.");
        return result.AsReadOnly();
    }

    private static int CompareDescriptors(
        ElinModifierModuleDescriptor left,
        ElinModifierModuleDescriptor right)
    {
        var order = left.Order.CompareTo(right.Order);
        return order != 0
            ? order
            : string.Compare(left.Id, right.Id, StringComparison.Ordinal);
    }
}
