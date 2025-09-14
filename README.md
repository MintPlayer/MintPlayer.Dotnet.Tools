# .NET Tools
## Source Generators
This repository contains several .NET Source generators
- [ValueComparerGenerator](SourceGenerators/ValueComparerGenerator/README.md): Makes it easier to write your own source-generators by generating the value-comparers for you
- [MapperGenerator](SourceGenerators/Mapper/README.md): Automatically generates mapper-extension-methods for you. It has support for property-name remapping and property-type remapping
- [SourceGenerators](SourceGenerators/SourceGenerators/README.md)
    - Generates extension methods to register services decorated with the `[Register]` attribute
    - Allows you to use the `[Inject]` attribute, removing the constructor completely
    - Contains an interface-implementation analyzer