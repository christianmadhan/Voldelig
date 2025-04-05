using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VoldeligClient
{

    /// <summary>
    /// Marker interface for entities that might contain table data
    /// and provides a method to populate it from a JToken.
    /// </summary>
    public interface ICanPopulateTable
    {
        /// <summary>
        /// Populates the entity's table data from the provided JToken,
        /// typically representing the 'panes.table.records' array.
        /// </summary>
        /// <param name="tableToken">The JToken containing the array of table records.</param>
        void PopulateTableFromJson(JToken tableToken);
    }

    /// <summary>
    /// Represents an entity holding a table of TLine items.
    /// Provides a default implementation for populating the table.
    /// </summary>
    /// <typeparam name="TLine">The type of the items in the table.</typeparam>
    public interface IHasTableData<TLine> : ICanPopulateTable where TLine : class, new()
    {
        List<TLine> Table { get; set; } // Implementing class MUST provide this property

        // Default implementation of the method from ICanPopulateTable
        // This logic runs if the implementing class doesn't provide its own version.
        void ICanPopulateTable.PopulateTableFromJson(JToken tableToken)
        {
            if (tableToken == null || tableToken.Type != JTokenType.Array)
            {
                this.Table = new List<TLine>(); // 'this' refers to the implementing class instance
                return;
            }

            var tableList = new List<TLine>();
            var lineProperties = typeof(TLine).GetProperties().Where(p => p.CanWrite).ToList(); // Cache properties

            foreach (JToken recordToken in tableToken)
            {
                JToken dataToken = recordToken.SelectToken("data");
                if (dataToken == null || dataToken.Type != JTokenType.Object) continue;

                TLine newLine = new TLine();
                var dataObject = (JObject)dataToken;

                foreach (var prop in lineProperties)
                {
                    if (dataObject.TryGetValue(prop.Name, StringComparison.OrdinalIgnoreCase, out JToken propValue))
                    {
                        try
                        {
                            // Use JToken.ToObject for robust conversion
                            object value = propValue.ToObject(prop.PropertyType);
                            prop.SetValue(newLine, value);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"WARN: Error converting property {prop.Name} for {typeof(TLine).Name}: {ex.Message}. Raw value: '{propValue}'");
                            // Optional: Add fallback logic (e.g., try ToString if property is string)
                            if (prop.PropertyType == typeof(string))
                            {
                                try { prop.SetValue(newLine, propValue.ToString()); } catch { }
                            }
                        }
                    }
                }
                tableList.Add(newLine);
            }
            this.Table = tableList; // Assign the populated list to the instance's property
        }
    }
    public interface IInstances
    {
        string InstancesJObject();
        string FilterJObject(string expr, int limit);
    }

    public abstract class InstancesBase : IInstances
    {
        public virtual string InstancesJObject()
        {
            var properties = GetType().GetProperties().Where(p => p.CanRead).ToList();
            var cardFields = properties
            .Where(p => !p.Name.Equals("Table", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Name.ToLower())
            .ToArray();

            JObject jsonObject = new JObject
            {
                ["panes"] = new JObject
                {
                    ["card"] = new JObject { ["fields"] = new JArray(cardFields) }
                }
            };

            var tableProperty = properties.FirstOrDefault(p => p.Name.Equals("Table", StringComparison.OrdinalIgnoreCase));
            if (tableProperty != null && tableProperty.PropertyType.IsGenericType)
            {
                var tableType = tableProperty.PropertyType.GetGenericArguments().FirstOrDefault();
                if (tableType != null)
                {
                    var tableFields = tableType.GetProperties()
                        .Where(p => p.CanRead)
                        .Select(p => p.Name.ToLower())
                        .ToArray();

                    ((JObject)jsonObject["panes"]).Add("table", new JObject { ["fields"] = new JArray(tableFields) });
                }
            }

            return jsonObject.ToString();
        }

        public virtual string FilterJObject(string expr, int limit)
        {
            var fields = GetType().GetProperties()
                .Where(p => p.CanRead)
                .Select(p => p.Name.ToLower())
                .ToArray();

            JObject jsonObject = new JObject
            {
                ["restriction"] = expr,
                ["fields"] = new JArray(fields),
                ["limit"] = limit
            };
            return jsonObject.ToString();
        }
    }

}
