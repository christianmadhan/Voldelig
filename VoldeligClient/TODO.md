## TODO


CARD -

* get the table, for example, if you want employee revision lines from employee - when you use the ActionType.GET
  and you have specified in your employee class that you want the table.

* print document needs to be implemented. I think there should be a field on the class e.g. ShowJobInvoice.cs that contains the bytearray
  after the print action have been runned.

* figure out a way to handle other actions which is not part of the enum list. Or update the method to be more generic?

FILTER -

* I believe the last maconomy specific type that we are missing is amount -> double, 
	* also time(11,11,11) like date, just need to find a container where you can filter on that


## MAYBE?

What if we want to filter for example the employees table by all employees created today, and then update everyone in that list.

like so: 

var empData = new Employee { name1 = "TEST" };
Expression<Func<Employees, bool>> predicate = e => e.CreatedDate > DateTime.Now.AddYears(-1);
var employeeList = await client.Authenticate().Filter(predicate, limit: 0).UpdateAll(empData);

Which would then return a list of updated entries, or a list of httpresponse messages where it went wrong.

--------------------------------------------------------------

