
using Microsoft.Extensions.Configuration;
using System.Linq.Expressions;
using System.Threading.Tasks;
using VoldeligClient;
using Xunit;
using static VoldeligTest.MaconomyModel.MaconomyEnums;


public class FilterTest
{


    private IConfiguration _configuration;


    public FilterTest()
    {
        // Load appsettings.json for test environment
        // poor. :(
        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())  // Ensure correct path
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

        _configuration = builder.Build();
    }

    [Fact]
    public async Task FilterTestGetAllCreatedAfterOneYearAgo0()
    {
        var client = new Voldelig(_configuration);
        Expression<Func<Employees, bool>> predicate = e => e.CreatedDate > DateTime.Now.AddYears(-1);

        var employeeList = await client.Authenticate().Filter(predicate, limit: 0);
        List<Employees> employees = employeeList.Match(
            list => list, // If successful, return the list
            errorResponse =>
            {
                // Handle the error (e.g., logging or throwing an exception)
                Console.WriteLine($"Request failed: {errorResponse.MaconomyErrorMessage}");
                return new List<Employees>(); // Return an empty list as a fallback
            }
         );
        Assert.True(employees.Count() > 0);
    }


    [Fact]
    public async Task FilterTestGetAllFemales()
    {
        var client = new Voldelig(_configuration);
        Expression<Func<Employees, bool>> predicate = e =>
        e.CreatedDate > new DateTime(2019, 1, 1) && e.Gender == GenderType.FEMALE;

        var employeeList = await client.Authenticate().Filter(predicate);
        List<Employees> employees = employeeList.Match(
            list => list, // If successful, return the list
            errorResponse =>
            {
                // Handle the error (e.g., logging or throwing an exception)
                Console.WriteLine($"Request failed: {errorResponse.MaconomyErrorMessage}");
                return new List<Employees>(); // Return an empty list as a fallback
            }
         );
        Assert.True(employees.Count > 0);
    }
}