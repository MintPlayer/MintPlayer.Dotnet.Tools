using System.Runtime.Serialization;

namespace WidgetApi.Contracts;

[DataContract(Name = nameof(CreateWidget), Namespace = "http://schemas.example.com/widgets")]
public class CreateWidget
{
    [DataMember(Order = 1)] public string Name { get; set; } = default!;
    [DataMember(Order = 2)] public string Color { get; set; } = default!;
}

[DataContract(Name = nameof(WidgetDto), Namespace = "http://schemas.example.com/widgets")]
public class WidgetDto
{
    [DataMember(Order = 1)] public string Id { get; set; } = default!;
    [DataMember(Order = 2)] public string Name { get; set; } = default!;
    [DataMember(Order = 3)] public string Color { get; set; } = default!;
}

[DataContract(Name = nameof(WidgetCreatedResponse), Namespace = "http://schemas.example.com/widgets")]
public class WidgetCreatedResponse
{
    [DataMember(Order = 1)] public WidgetDto Widget { get; set; } = default!;
    [DataMember(Order = 2)] public Dictionary<string, string> RequestHeaders { get; set; } = default!;
}