namespace RabWorker.Contracts;

/// <summary>Patchable form fields. Names double as JSON keys and person_form column names.</summary>
public static class FormFields
{
    public const string FirstName = "first_name";
    public const string LastName = "last_name";
    public const string Email = "email";
    public const string Phone = "phone";
    public const string DateOfBirth = "date_of_birth";
    public const string AddressLine1 = "address_line1";
    public const string City = "city";
    public const string State = "state";
    public const string PostalCode = "postal_code";

    public static readonly string[] All =
        [FirstName, LastName, Email, Phone, DateOfBirth, AddressLine1, City, State, PostalCode];
}
