using MapAsDictionaryDebugging;
using MintPlayer.Mapper.Attributes;

var personObj = new PersistentObject
{
    Id = 1,
    Name = "Person",
    Attributes =
    [
        new SimplePersistentObjectAttribute
        {
            Id = 1,
            Name = "FirstName",
            Value = "John"
        },
        new SimplePersistentObjectAttribute
        {
            Id = 1,
            Name = "LastName",
            Value = "Doe"
        },
        new SimplePersistentObjectAttribute
        {
            Id = 1,
            Name = "BirthDay",
            Value = "1980-01-01"
        },
        new PersistentObjectAttributeWithReference
        {
            Id = 1,
            Name = "Address",
        },
    ]
};
var person = personObj.MapToPerson();

var addressObj = new PersistentObject
{
    Id = 2,
    Name = "Address",
    Attributes =
    [
        new SimplePersistentObjectAttribute
        {
            Id = 2,
            Name = "Street",
            Value = "123 Main St"
        },
        new SimplePersistentObjectAttribute
        {
            Id = 2,
            Name = "City",
            Value = "Anytown"
        },
        new SimplePersistentObjectAttribute
        {
            Id = 2,
            Name = "State",
            Value = "CA"
        },
        new SimplePersistentObjectAttribute
        {
            Id = 2,
            Name = "ZipCode",
            Value = "12345"
        },
    ]
};
var address = addressObj.MapToAddress();

var personObjBack = person.MapToPersistentObject();
var addressObjBack = address.MapToPersistentObject();

namespace MapAsDictionaryDebugging
{
    [GenerateMapper(typeof(PersistentObject))]
    public class Person
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public DateOnly BirthDay { get; set; }
        public Address Address { get; set; }
    }

    [GenerateMapper(typeof(PersistentObject))]
    public class Address
    {
        public string Street { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public string ZipCode { get; set; }
    }

    [MapAsDictionary]
    public class PersistentObject
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public PersistentObjectAttribute[] Attributes { get; set; }
        public PersistentObjectAttribute? this[string attribute] => Attributes.SingleOrDefault(a => a.Name == attribute);

        public override string ToString() => $"PersistentObject: Id={Id}, Name={Name}";
    }

    [MapAsDictionary]
    public abstract class PersistentObjectAttribute
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public abstract PersistentObject? this[string attribute] { get; }


        public override string ToString() => $"PersistentObjectAttribute: Id={Id}, Name={Name}";
    }

    public class SimplePersistentObjectAttribute : PersistentObjectAttribute
    {
        public string? Value { get; set; }
        public override PersistentObject? this[string attribute] => throw new NotImplementedException();

        public override string ToString() => $"SimplePersistentObject: Id={Id}, Name={Name}, Value={Value}";
    }

    public class PersistentObjectAttributeWithReference : PersistentObjectAttribute
    {
        public override PersistentObject? this[string attribute] => 
            Reference is null ? null
            : Reference[attribute] is PersistentObjectAttributeWithReference po ? po.Reference
            : null;

        public PersistentObject? Reference { get; set; }
        public override string ToString() => $"PersistentObjectAttributeAsReference: Id={Id}, Name={Name}, Value=({Reference})";
    }
}