# component_dependency_auto_generator

The Component Dependency Auto Generator is a Tridion core service application that looks through a given publication to resolve all dependencies among schemas and create sample components for all schemas, according to an ordering defined by said dependencies.

Note: The implementation of ComponentCreator is complicated by the possibility of some more obscure sets of schemas that may exist (in particular, cases when no allowed type is selected for a field, which means any type is valid, and so we need to have a default component schema and a default multimedia schema identified as soon as possible). For example, say there is a very simple multimedia schema which gets set as the default multimedia schema. Then say there is a component schema with a multimedia link and no allowed type. Because the default multimedia schema has been set, we can simply use that. However, now say there is another component schema with a component link and no specified allowed type. Now the first component must have been identified as the default for this to work. Although slightly complicated, the current implementation of the ComponentCreator should handle all such obscure cases. The algorithm currently checks near the beginning for schemas with no dependencies, and there should always be at least one. Then, as components are created, both a default component link schema and a default mutlimedia link schema will eventually be identified and used as needed.

The current implementation attempts to find all dependencies for all schemas before beginning to create the components. This further complicates matters, slightly. A better solution would perhaps be to create components recursively by checking if any dependencies exist and if not, making a recursive function call to create them first (in other words, resolve dependencies on the fly and not in advance).

Currently the following schema fields are supported (as both regular fields and metadata fields):

Text fields (including rich text fields)
Date fields
Number fields
Component links
Multimedia component links
Embedded schemas
