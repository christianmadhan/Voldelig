
using Microsoft.Extensions.Configuration;
using System.Linq.Expressions;
using System.Threading.Tasks;
using VoldeligClient;
using Xunit;


public class CardAndFilterTest
{


    private IConfiguration _configuration;


    public CardAndFilterTest()
    {
        // Load appsettings.json for test environment
        // poor. :(
        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())  // Ensure correct path
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

        _configuration = builder.Build();
    }

    [Fact]
    public async Task FilterTestClientFromConfigClass()
    {
        var client = new Voldelig(_configuration);
        Expression<Func<Employees, bool>> predicate = e => e.Gender != GenderType.MALE;

        var employeeList = await client.Authenticate().Filter(predicate);
        List<Employees> employees = employeeList.Match(
            list => list, // If successful, return the list
            errorResponse =>
            {
                // Handle the error (e.g., logging or throwing an exception)
                Console.WriteLine($"Request failed: {errorResponse.StatusCode}");
                return new List<Employees>(); // Return an empty list as a fallback
            }
         );
        Assert.True(employees.Count() > 0);
    }


    [Fact]
    public async Task CardTestClientFromConfigClass()
    {
        var clientFromConfig = new Voldelig(_configuration);
        var employee = new Employees() { EmployeeNumber = "101001" };

        var authentedClient = await clientFromConfig.Authenticate().Card(ActionType.Get, employee);
        Assert.True(authentedClient != null);
    }

    [Fact]
    public async Task CardTestClass()
    {
        var client = new Voldelig(_configuration);
        var employee = new Employees() { EmployeeNumber = "101001" };

        var authentedClient = await client.Authenticate().Card(ActionType.Get, employee);
        Assert.True(authentedClient != null);
    }

    //[Fact]
    //public async Task FilterTestClass()
    //{

    //    Expression<Func<Employees, bool>> predicate = e => 
    //    e.CreatedDate > new DateTime(2019, 1, 1) && e.Gender == GenderType.FEMALE;

    //    var employeeList = await client.Authenticate().Filter(predicate);
    //    List<Employees> employees = employeeList.Match(
    //        list => list, // If successful, return the list
    //        errorResponse =>
    //        {
    //            // Handle the error (e.g., logging or throwing an exception)
    //            Console.WriteLine($"Request failed: {errorResponse.StatusCode}");
    //            return new List<Employees>(); // Return an empty list as a fallback
    //        }
    //     );
    //    Assert.True(employees.Count > 0);  
    //}
}