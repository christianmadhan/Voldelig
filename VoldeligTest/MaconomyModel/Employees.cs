using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using static Helper;
using static VoldeligClient.Voldelig;
using static VoldeligTest.MaconomyModel.MaconomyEnums;

// Class name should be the same as the container name, because thats how the method that calls either
// Card or filter find out which container to call.
public class Employees
{
    [KeyField]
    public string EmployeeNumber { get; set; }
    public string Name1 { get; set; }
    public string Name2 { get; set; }
    public DateTime CreatedDate { get; set; }

    [JsonConverter(typeof(SafeEnumConverter<CountryType>))]
    public CountryType Country { get; set; }
    [JsonConverter(typeof(SafeEnumConverter<GenderType>))]
    public GenderType Gender { get; set; }
    public static string InstancesJObject()
    {
        JObject jsonObject = new JObject
        {
            ["panes"] = new JObject
            {
                ["card"] = new JObject
                {
                    ["fields"] = new JArray
                            {
                                "employeenumber",
                                "name1",
                                "name2",
                                "country",
                                "gender",
                                "createddate",
                            }
                }
            }
        };
        return jsonObject.ToString();
    }

    public static string FilterJObject(string expr, int limit)
    {
        JObject jsonObject = new JObject
        {
            ["restriction"] = $"{expr}",
            ["fields"] = new JArray
                    {
                        "employeenumber",
                        "name1",
                        "name2",
                        "country",
                        "gender",
                        "createddate",
                    },
            ["limit"] = limit
        };
        return jsonObject.ToString();
    }

}