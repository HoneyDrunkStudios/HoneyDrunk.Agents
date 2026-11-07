# HoneyDrunk.Agents

HoneyDrunk.Agents defines how hosts register and execute agents on the HoneyDrunk Grid.

The initial [Abstractions project](src/HoneyDrunk.Agents.Abstractions/README.md) defines the execution, tool, and memory contracts. The [contract baseline](api/HoneyDrunk.Agents.Abstractions.contract-shape.txt) tracks the agent-facing API. Host lifecycle implementation, authorized tool dispatch, and scoped memory are subsequent runtime work.

Validate with the .NET 10 SDK.

```sh
dotnet test HoneyDrunk.Agents.slnx -c Release
```
