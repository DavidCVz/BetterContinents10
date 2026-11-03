using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BC = BetterContinents.BetterContinents;

namespace SeedPreviewerReference
{
    // Reuse the mod's JSON writer; no serializer assembly absent from the game is required.
    public static class ReferenceJson
    {
        static readonly Type Writer = typeof(BC).Assembly.GetType("BetterContinents.WorldExportJson", true);
        public static string Serialize(object value) => (string)Writer.GetMethod("Write", BindingFlags.Public | BindingFlags.Static,
            null, new[] { typeof(object) }, null).Invoke(null, new[] { ConvertValue(value) });

        static object ConvertValue(object value)
        {
            if (value == null || value is string || value.GetType().IsPrimitive) return value;
            var obj = (ICollection<KeyValuePair<string, object>>)Activator.CreateInstance(Writer.GetNestedType("Obj"));
            if (value is IDictionary dictionary)
            {
                foreach (DictionaryEntry entry in dictionary) obj.Add(new KeyValuePair<string, object>((string)entry.Key, ConvertValue(entry.Value)));
                return obj;
            }
            if (value is IEnumerable sequence) return sequence.Cast<object>().Select(ConvertValue).ToArray();
            foreach (var property in value.GetType().GetProperties()) obj.Add(new KeyValuePair<string, object>(property.Name, ConvertValue(property.GetValue(value))));
            return obj;
        }
    }
}
