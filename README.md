# HoneyDrunk.Agents

HoneyDrunk.Agents defines how hosts register and execute agents on the HoneyDrunk Grid.

The [Abstractions project](src/HoneyDrunk.Agents.Abstractions/README.md) defines the execution, tool, and memory contracts. The [contract baseline](api/HoneyDrunk.Agents.Abstractions.contract-shape.txt) tracks the agent-facing API. The [runtime project](src/HoneyDrunk.Agents/README.md) registers and runs agents in process, checks tools through Operator and Capabilities, and gives each run scoped Memory access.

Validate with the .NET 10 SDK.

```sh
dotnet test HoneyDrunk.Agents.slnx -c Release
```
