# HoneyDrunk.Agents.Testing

Test fixtures for agents. `AgentTestHarness` runs the real lifecycle over the Capabilities test harness, the Memory runtime on the in-memory provider, and in-memory Operator gates, and exposes each piece for assertions. `GrantingAuthorizationPolicy` is for tests only and must never be registered in a production host.
