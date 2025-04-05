using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using VoldeligClient;
using static Helper;
using static VoldeligClient.Voldelig;
using static VoldeligTest.MaconomyModel.MaconomyEnums;

// Class name should be the same as the container name, because thats how the method that calls either
// Card or filter find out which container to call.
public class Employees : InstancesBase, IInstances, IHasTableData<EmployeeTableLine>
{
    [KeyField]
    public string EmployeeNumber { get; set; }
    public string Name1 { get; set; }
    public string Name2 { get; set; }
    public DateTime CreatedDate { get; set; }

    public List<EmployeeTableLine> Table { get; set; }

    [JsonConverter(typeof(SafeEnumConverter<CountryType>))]
    public CountryType Country { get; set; }
    [JsonConverter(typeof(SafeEnumConverter<GenderType>))]
    public GenderType Gender { get; set; }
}

public class EmployeeTableLine 
{
    [KeyField]
    public string EmployeeNumber { get; set; }
    [KeyField]
    public string FromDate { get; set; }
    public string Telephone { get; set; }
    public string CNRNumber{ get; set; }
}