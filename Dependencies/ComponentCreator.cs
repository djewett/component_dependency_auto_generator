using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Tridion.ContentManager.CoreService.Client;

namespace CoreService.ComponentDependencies
{
    class ComponentCreator
    {
        // TODOs:
        //    - Handle/test case where component exists already
        //        (probably best to append something to existing naming scheme
        //        in that case)
        //    - In addition to <summary> tags, add <param> tags and <return>
        //        tags to documentations:
        //        /// <param name="resolved"></param>
        //        /// <param name="deps"></param>
        //        /// <returns></returns>
        //    - Retrieve Jpeg TCM ID frm Tridion instead of hard-coding

        /// <summary>
        /// Client used to interact with the core service.
        /// </summary>
        private SessionAwareCoreServiceClient client;

        /// <summary>
        /// Used to ensure unique naming for new components.
        /// </summary>
        private int componentCount;

        /// <summary>
        /// Filepath for a dummy image that will be used to populate multimedia
        /// components.
        /// </summary>
        private string dummyImageFilePath;

        /// <summary>
        /// Used for mandatory component link fields with no listed allowed 
        /// types (in which case any type is allowed); the algorithm attempts 
        /// to setup this default in several places, but in some situations, no
        /// default will be set.
        /// </summary>
        private string defaultSchemaUriForCompLink;

        /// <summary>
        /// Used for mandatory multimedia component link fields with no listed 
        /// allowed types (in which case any type is allowed); the algorithm 
        /// attempts to set up this default in several places, but in some 
        /// situations, no default will be set.
        /// </summary>
        private string defaultSchemaUriForMMCompLink;

        /// <summary>
        /// Maintain a mapping from each schema's TCM ID to the TCM ID of
        /// the component that is created for that schema.
        /// </summary>
        private Dictionary<string, string> schemaCompIdMapping;

        /// <summary>
        /// Maintains a set of resolved dependencies for quick comparison to
        /// determine whether a component can be created based on whether its
        /// dependencies have all been created.
        /// </summary>
        private HashSet<string> resolvedDependencies;

        /// <summary>
        /// This variable represents the order in which components should be 
        /// created from shemas; it is a queue of schema TCM IDs. The order is 
        /// defined here as a member variable to allow manipulating it as the 
        /// first component and multimedia component are created.
        /// </summary>
        private Queue<string> order;

        /// <summary>
        /// This is a hard-coded TCM ID of the JPG multimedia type, which can
        /// be found in Tridion under Multimedia Types by hovering over 
        /// "Jpeg image". This value can also be retrieved from Tridion instead
        /// of hard-coding its value by retrieving a list of item types, etc.
        /// </summary>
        private const string JpgTcmId = "tcm:0-2-65544";

        /// <summary>
        /// Constructor method used to set up a dependency resolver.
        /// </summary>
        public ComponentCreator(SessionAwareCoreServiceClient client,
                                  string dummyImage = null)
        {
            this.client = client;
            schemaCompIdMapping = new Dictionary<string, string>();
            dummyImageFilePath = dummyImage;
        }

        /// <summary>
        /// Given a Publication URI, this method returns an ordering (queue) of
        /// schema TCM IDs representing the order in which corresponding 
        /// components can be created from the publication's schemas.
        /// </summary>
        public Queue<string> Resolve(string pubUri, string folderForComps)
        {
            // Initialize order to an empty queue:
            order = new Queue<string>();

            // Initialize resolvedDependencies to an empty set:
            resolvedDependencies = new HashSet<string>();

            // Get only the schemas in the Publication:
            RepositoryItemsFilterData filter =
                new RepositoryItemsFilterData 
                    { ItemTypes = new[] { ItemType.Schema }, Recursive = true };
            XElement schemas = client.GetListXml(pubUri, filter);

            // TODO: Use Lambda expression instead to retrieve schemaDataList:

            // Build a list of schema data as List<SchemaData> schemas:
            var schemaDataList = new List<SchemaData>();
            foreach (XElement schema in schemas.Elements())
            {
                string schemaUri = schema.Attribute("ID").Value;
                SchemaData schemaData =
                    (SchemaData)client.Read(schemaUri, new ReadOptions());

                // Add only component schemas and multimedia schemas to list:
                if (schemaData.Purpose == SchemaPurpose.Component ||
                    schemaData.Purpose == SchemaPurpose.Multimedia)
                {
                    schemaDataList.Add(schemaData);
                }
            }

            // Attempts to setup defaultSchemaUriForCompLink and 
            // defaultSchemaUriForMMCompLink for use by schemas that contain
            // links to unspecified types:
            SetupDefaultComponents(schemaDataList);

            // TODO: Instead of enqueuing null, enqueue a string to represent a 
            // default not yet found:

            // After setting up defaults, we expect either the default Schema
            // to use for component links or the default schema to use for
            // multimedia links to be set. The other default may still be null,
            // which we handle later in the main while loop by enqueueing null
            // to represent the remaining null default getting resolved. If one
            // of the defaults is null, there may be some temporary dependencies
            // on null (representing that the default has not be set up yet):
            if (null == defaultSchemaUriForCompLink &&
                null == defaultSchemaUriForMMCompLink)
            {
                throw new Exception("Expecting at least one default to be set");
            }

            // Build a mapping of schema IDs to dependency lists; each list 
            // contains the TCM IDs of all schemas that the given schema 
            // depends on:
            var depMap = new Dictionary<string, List<string>>();
            for (int i = 0; i < schemaDataList.Count; i++)
            {
                List<string> dependencies = new List<string>();
                GetDependencies(schemaDataList[i], ref dependencies);
                depMap.Add(schemaDataList[i].Id, dependencies);
            }

            // Initialize componentCount to 1 (used for naming convention):
            componentCount = 1;

            // Resolve dependencies and create components: repeatedly iterate
            // over all remaining schemas that have not yet been resolved and
            // maintain an order (queue) of the ones that have been resolved:
            int numIterations = 0;
            int maxIterations = schemaDataList.Count;
            while (schemaDataList.Count > 0)
            {
                // If dependencies cannot be resolved within maxIterations,
                // then there is most likely a problem with the schemas,
                // such as a circular dependency:
                if (numIterations >= maxIterations)
                {
                    throw new Exception("Problem resolving dependencies");
                }

                // Iterate over schema list in REVERSE so we can safely remove
                // elements as we go:
                for (int i = schemaDataList.Count - 1; i >= 0; i--)
                {
                    // TODO: Faster way to retrieve element at index i?:
                    SchemaData currSD = schemaDataList.ElementAt(i);

                    if (ContainsAll(resolvedDependencies, depMap[currSD.Id]))
                    {
                        // Order maintains the queue order in which components
                        // should be created from schemas, and is returned; 
                        // resolvedDependencies maintains the same schema IDs, 
                        // but as a hashset for quicker containment comparisons
                        // (queue => O(n) vs hashset => O(1)):
                        order.Enqueue(currSD.Id);
                        resolvedDependencies.Add(currSD.Id);

                        string newCompId = CreateComponent(folderForComps, 
                                                           currSD);

                        CheckForDefaults(currSD);

                        schemaCompIdMapping.Add(currSD.Id, newCompId);

                        // Fast (O(1)) since we are removing from the end:
                        schemaDataList.RemoveAt(i);
                    }
                }

                numIterations++;
            }

            return order;
        }

        /// <summary>
        /// This helper method checks if one of the default schemas has not
        /// been set and sets it accordingly. It adds null as a placeholder
        /// in the resolvedDependencies set to indicate the remaining default 
        /// has been set (for any schemas that already contain a dependency on 
        /// the given default, which in that case would be represented by null):
        /// </summary>
        private void CheckForDefaults(SchemaData sd)
        {
            if (null == defaultSchemaUriForCompLink &&
                SchemaPurpose.Component == sd.Purpose)
            {
                resolvedDependencies.Add(null);
                defaultSchemaUriForCompLink = sd.Id;
            }
            else if (null == defaultSchemaUriForMMCompLink &&
                     SchemaPurpose.Multimedia == sd.Purpose)
            {
                resolvedDependencies.Add(null);
                defaultSchemaUriForMMCompLink = sd.Id;
            }
        }

        /// <summary>
        /// This helpder method creates a component in the folder given by
        /// folderForCompsUri with the schema given by sd and an existing
        /// mapping of schemas to their newly created components given by
        /// schemaCompMap:
        /// </summary>
        private string CreateComponent(string folderForCompsUri,
                                       SchemaData sd)
        {
            ComponentData newCompData =
                client.GetDefaultData(ItemType.Component,
                                      folderForCompsUri,
                                      new ReadOptions()) as ComponentData;

            // Create naming scheme for new components here:
            // Currently naming is based on the schema title as well as the
            // number of components created so far and follows the pattern:
            // "NewComponent_<schema-title>_<number-of-components>"
            newCompData.Title =
                "NewComponent_" + sd.Title + "_" + componentCount++;

            newCompData.Schema.IdRef = sd.Id;

            SchemaFieldsData sfData =
                client.ReadSchemaFields(sd.Id, false, null);

            // Use schema's namespace for new component:
            XNamespace ns = sd.NamespaceUri;

            switch (sd.Purpose)
            {
                case SchemaPurpose.Component:
                    {
                        // Component schemas have both "Design" and "Metadata
                        // Design" tabs:

                        // "Design" tab:

                        ItemFieldDefinitionData[] fields = sfData.Fields;

                        string procsFieldsXElementAsString =
                            ProcessFields(ns, fields);

                        newCompData.Content = procsFieldsXElementAsString;

                        // "Metadata Design" tab:

                        ItemFieldDefinitionData[] mdFields =
                            sfData.MetadataFields;

                        string procsMDFieldsXElementAsString =
                            ProcessMDFields(ns, mdFields);

                        newCompData.Metadata = procsMDFieldsXElementAsString;

                        break;
                    }
                case SchemaPurpose.Multimedia:
                    {
                        // Multimedia schemas have only "Metadata Design" tab:

                        ItemFieldDefinitionData[] mdFields =
                            sfData.MetadataFields;

                        string procsMDFieldsXElementAsString =
                            ProcessMDFields(ns, mdFields);

                        newCompData.Metadata = procsMDFieldsXElementAsString;

                        if (null == dummyImageFilePath)
                        {
                            throw new Exception("Dummy image file undefined");
                        }

                        newCompData.BinaryContent = new BinaryContentData
                        {
                            Filename = "xxxxxxxx.jpg",
                            MultimediaType = new LinkToMultimediaTypeData() 
                                                 { IdRef = JpgTcmId },
                            UploadFromFile = dummyImageFilePath
                        };

                        newCompData.ComponentType = ComponentType.Multimedia;

                        break;
                    }
                default:
                    {
                        throw new Exception("Unexpected schema purpose");
                    }
            }

            string test = newCompData.Metadata;

            string newCompUri =
                client.Create(newCompData, new ReadOptions()).Id;

            return newCompUri;
        }

        /// <summary>
        /// This helper method returns true iff resolved list contains all 
        /// dependencies in deps list:
        /// </summary>
        private bool ContainsAll(HashSet<string> resolved, List<String> deps)
        {
            // Can use intersection of resolved with deps, but this may be
            // faster since it stops as soon as an element deps not contained
            // in resolved is found:

            bool dependenciesAreContained = true;

            foreach (string dep in deps)
            {
                if (!resolved.Contains(dep))
                {
                    dependenciesAreContained = false;
                    break;
                }
            }

            return dependenciesAreContained;
        }

        /// <summary>
        /// This recursive helper method processes a schema field, recursing on
        /// Embedded schemas as necessary
        /// </summary>
        private XElement ProcessField(XNamespace ns,
                                      ItemFieldDefinitionData field)
        {
            XElement processedField;

            // TODO: Consider using field.GetType().name here and using a 
            // switch statement ('is' uses try-catch, throwing exceptions,
            // which may be expensive):

            if (field is SingleLineTextFieldDefinitionData &&
                field.MinOccurs > 0)
            {
                processedField = new XElement(ns + field.Name, "xxxxxxxx");
            }
            else if(field is NumberFieldDefinitionData &&
                    field.MinOccurs > 0)
            {
                processedField = new XElement(ns + field.Name, "00000000");
            }
            else if (field is DateFieldDefinitionData &&
                     field.MinOccurs > 0)
            {
                processedField =
                    new XElement(ns + field.Name, "2014-08-29T00:36:04");
            }
            else if (field is ExternalLinkFieldDefinitionData &&
                     field.MinOccurs > 0)
            {
                processedField =
                    GetExternalLinkField(ns, field, "https://www.google.ca/");
            }
            else if (field is ComponentLinkFieldDefinitionData &&
                     field.MinOccurs > 0)
            {
                ComponentLinkFieldDefinitionData currFieldAsCL =
                    (ComponentLinkFieldDefinitionData)field;

                string depUri;
                if (currFieldAsCL.AllowedTargetSchemas.Count() > 0)
                {
                    // If multiple allowed targets, select the first:
                    depUri = currFieldAsCL.AllowedTargetSchemas
                             .ElementAt(0).IdRef;
                }
                else
                {
                    // No allowed target schemas means any component is valid
                    // to link to; so use default:
                    depUri = defaultSchemaUriForCompLink;
                }

                // Look up the component that has been previously created from 
                // the schema:
                if (!schemaCompIdMapping.ContainsKey(depUri))
                {
                    throw new Exception("Dependency compoent does not exist");
                }

                string linkedCompUri = schemaCompIdMapping[depUri];

                processedField = GetCompOrMMLinkField(ns, field, linkedCompUri);
            }
            else if (field is MultimediaLinkFieldDefinitionData &&
                     field.MinOccurs > 0)
            {
                MultimediaLinkFieldDefinitionData currFieldAsMML =
                    (MultimediaLinkFieldDefinitionData)field;

                string depUri;
                if (currFieldAsMML.AllowedTargetSchemas.Count() > 0)
                {
                    // If multiple allowed targets, select the first:
                    depUri = currFieldAsMML.AllowedTargetSchemas.
                             ElementAt(0).IdRef;
                }
                else
                {
                    // No allowed target schemas means any multimedia component
                    // is valid to link to; so use default:
                    depUri = defaultSchemaUriForMMCompLink;
                }

                // Look up the component that has been previously created from 
                // the schema:
                if (!schemaCompIdMapping.ContainsKey(depUri))
                    throw new Exception("Dependency compoent does not exist");
                string linkedCompUri = schemaCompIdMapping[depUri];

                processedField = GetCompOrMMLinkField(ns, field, linkedCompUri);
            }
            else if (field is EmbeddedSchemaFieldDefinitionData &&
                     field.MinOccurs > 0)
            {
                EmbeddedSchemaFieldDefinitionData currFieldAsES =
                    (EmbeddedSchemaFieldDefinitionData)field;

                string embSchemaId = currFieldAsES.EmbeddedSchema.IdRef;

                SchemaFieldsData embSfData =
                    client.ReadSchemaFields(embSchemaId, false, null);

                ItemFieldDefinitionData[] embFields = embSfData.Fields;

                processedField = new XElement(ns + currFieldAsES.Name);

                // TODO: Test case with embedded schema with no fields:

                foreach (var embField in embFields)
                {
                    processedField.Add(ProcessField(ns, embField));
                }
            }
            else
            {
                // Empty XML tag:
                processedField = new XElement(ns + field.Name);
            }

            return processedField;
        }

        /// <summary>
        /// This helper method processes regular schema fields and returns an 
        /// XElement (as a string) representation of them:
        /// Note: For empty content (as opposed to metadata), an empty 
        /// content tag is returned
        /// </summary>
        private string ProcessFields(XNamespace ns,
                                     ItemFieldDefinitionData[] fields)
        {
            var processedFieldsXE = new XElement(ns + "Content");

            if (fields != null && fields.Count() > 0)
            {
                for (int i = 0; i < fields.Length; i++)
                {
                    ItemFieldDefinitionData currField = fields[i];
                    processedFieldsXE.Add(ProcessField(ns, currField));
                }
            }

            return processedFieldsXE.ToString();
        }

        /// <summary>
        /// This helper method processes metadata schema fields and returns an 
        /// XElement (as a string) representation of them:
        /// Note: For empty metadata (as opposed to content), an empty string 
        /// is returned
        /// </summary>
        private string ProcessMDFields(XNamespace ns,
                                       ItemFieldDefinitionData[] fields)
        {
            string processedFields = "";

            if (fields != null && fields.Count() > 0)
            {
                XElement processedFieldsXE = new XElement(ns + "Metadata");

                for (int i = 0; i < fields.Length; i++)
                {
                    ItemFieldDefinitionData currField = fields[i];
                    processedFieldsXE.Add(ProcessField(ns, currField));
                }

                processedFields = processedFieldsXE.ToString();
            }

            return processedFields;
        }

        /// <summary>
        /// This helper method returns an XElement represetation of a given 
        /// component or multimedia link based on a namespace, field data and
        /// the link URI of the linked component:
        /// </summary>
        private XElement GetCompOrMMLinkField(XNamespace ns,
                                              ItemFieldDefinitionData currField,
                                              string linkedCompUri)
        {
            XNamespace ns_xlink = "http://www.w3.org/1999/xlink";

            ComponentData dataLinkedComp =
                (ComponentData)client.Read(linkedCompUri, new ReadOptions());

            var linkElem =
                new XElement(ns + currField.Name,
                    new XAttribute(XNamespace.Xmlns + "xlink", ns_xlink),
                    new XAttribute(ns_xlink + "type", "simple"),
                    new XAttribute(ns_xlink + "href", linkedCompUri),
                    new XAttribute(ns_xlink + "title", dataLinkedComp.Title));

            return linkElem;
        }

        /// <summary>
        /// This helper method returns an XElement represetation of a given 
        /// component or multimedia link based on a namespace, field data and
        /// the link URI of the linked component:
        /// </summary>
        private XElement GetExternalLinkField(XNamespace ns,
                                              ItemFieldDefinitionData currField,
                                              string extLinkURL)
        {
            XNamespace ns_xlink = "http://www.w3.org/1999/xlink";

            var linkElem =
                new XElement(ns + currField.Name,
                    new XAttribute(XNamespace.Xmlns + "xlink", ns_xlink),
                    new XAttribute(ns_xlink + "type", "simple"),
                    new XAttribute(ns_xlink + "href", extLinkURL));

            return linkElem;
        }

        /// <summary>
        /// This helper method contains some basic common logic to iterate over 
        /// fields and add the dependencies for each one:
        /// </summary>
        private void AddDependenciesForFields(ItemFieldDefinitionData[] fields,
                                              List<string> dependencies)
        {
            if (fields != null)
            {
                for (int i = 0; i < fields.Length; i++)
                {
                    ItemFieldDefinitionData currField = fields[i];
                    AddDependencyForSingleField(currField, dependencies);
                }
            }
        }

        /// <summary>
        /// This helper method adds to dependencies all dependencies derived
        /// from the single field (as TCM IDs of other schemas depended on):
        /// </summary>
        private void AddDependencyForSingleField(ItemFieldDefinitionData field,
                                                 List<string> dependencies)
        {
            string newDep = "";

            if (field is ComponentLinkFieldDefinitionData &&
                field.MinOccurs > 0)
            {
                ComponentLinkFieldDefinitionData currFieldAsCL =
                    (ComponentLinkFieldDefinitionData)field;

                if (currFieldAsCL.AllowedTargetSchemas.Count() > 0)
                {
                    // If multiple allowed targets, simply select the first one:
                    newDep = currFieldAsCL.AllowedTargetSchemas.
                             ElementAt(0).IdRef;
                }
                else
                {
                    newDep = defaultSchemaUriForCompLink;
                }

                if (!dependencies.Contains(newDep))
                {
                    dependencies.Add(newDep);
                }
            }
            else if (field is MultimediaLinkFieldDefinitionData &&
                     field.MinOccurs > 0)
            {
                MultimediaLinkFieldDefinitionData currFieldAsMML =
                    (MultimediaLinkFieldDefinitionData)field;

                if (currFieldAsMML.AllowedTargetSchemas.Count() > 0)
                {
                    // If multiple allowed targets, simply select the first one:
                    newDep = currFieldAsMML.AllowedTargetSchemas.
                             ElementAt(0).IdRef;
                }
                else
                {
                    newDep = defaultSchemaUriForMMCompLink;
                }

                if (!dependencies.Contains(newDep))
                {
                    dependencies.Add(newDep);
                }
            }
            else if (field is EmbeddedSchemaFieldDefinitionData &&
                     field.MinOccurs > 0)
            {
                EmbeddedSchemaFieldDefinitionData currFieldAsES =
                    (EmbeddedSchemaFieldDefinitionData)field;

                string embSchemaId = currFieldAsES.EmbeddedSchema.IdRef;

                SchemaData embSchemaData =
                    (SchemaData)client.Read(embSchemaId, new ReadOptions());

                // Recursive call on embedded schema data:
                GetDependencies(embSchemaData, ref dependencies);
            }
        }

        /// <summary>
        /// This helper method retrieves a list of dependencies (stored as TCM
        /// IDs of schemas depended on) of a given schema, sd (depenencies list
        /// is passed in by reference and populated):
        /// </summary>
        private void GetDependencies(SchemaData sd, 
                                     ref List<string> dependencies)
        {
            SchemaFieldsData schemaFieldsData =
                client.ReadSchemaFields(sd.Id, false, null);

            switch (sd.Purpose)
            {
                case SchemaPurpose.Component:
                case SchemaPurpose.Embedded:
                    {
                        // For Component and Embedded schemas, we want to look
                        // at regular fields as well as metadata fields:

                        ItemFieldDefinitionData[] fields =
                            schemaFieldsData.Fields;

                        AddDependenciesForFields(fields, dependencies);

                        ItemFieldDefinitionData[] mdFields =
                            schemaFieldsData.MetadataFields;

                        AddDependenciesForFields(mdFields, dependencies);

                        // If we come across a schema with no dependencies,
                        // set it as the default schema if there is no default 
                        // set yet:
                        if (0 == dependencies.Count() &&
                            null == defaultSchemaUriForCompLink)
                        {
                            defaultSchemaUriForCompLink = sd.Id;
                        }

                        break;
                    }
                case SchemaPurpose.Multimedia:
                    {
                        // For Multimedia schemas, we only want to look at 
                        // metadata fields:

                        ItemFieldDefinitionData[] mdFields =
                            schemaFieldsData.MetadataFields;

                        AddDependenciesForFields(mdFields, dependencies);

                        // If we come across a schema with no dependencies,
                        // set it as the default schema if there is no default 
                        // set yet:
                        if (0 == dependencies.Count() &&
                            null == defaultSchemaUriForMMCompLink)
                        {
                            defaultSchemaUriForMMCompLink = sd.Id;
                        }

                        break;
                    }
                default:
                    {
                        break; // Do nothing
                    }
            }
        }

        /// <summary>
        /// This helper method attemps to setup variables 
        /// defaultSchemaUriForCompLink and defaultSchemaUriForMMCompLink for
        /// use by component or multimedia link fields without any allowed types
        /// specified (indicating no explicit dependencies and that any type is 
        /// valid)
        /// </summary>
        private void SetupDefaultComponents(List<SchemaData> schemaDataList)
        {
            for (int i = 0; i < schemaDataList.Count; i++)
            {
                SchemaData sd = schemaDataList[i];

                SchemaFieldsData sfData =
                    client.ReadSchemaFields(sd.Id, false, null);

                ItemFieldDefinitionData[] fields = sfData.Fields;

                ItemFieldDefinitionData[] mdFields = sfData.MetadataFields;

                if (null == defaultSchemaUriForCompLink &&
                    !SchemaContainsLink(sd) &&
                    SchemaPurpose.Component == sd.Purpose)
                {
                    defaultSchemaUriForCompLink = sd.Id;

                    if (null != defaultSchemaUriForMMCompLink)
                    {
                        break;
                    }
                }
                else if (null == defaultSchemaUriForMMCompLink &&
                         !SchemaContainsLink(sd) &&
                         SchemaPurpose.Multimedia == sd.Purpose)
                {
                    defaultSchemaUriForMMCompLink = sd.Id;

                    if (null != defaultSchemaUriForCompLink)
                    {
                        break;
                    }
                }
                else
                {
                    // Do nothing
                }
            }
        }

        /// <summary>
        /// This helper method return true iff the given schema contains a 
        /// component link field, a multimedia link field or an embedded schema
        /// which contain such a field.
        /// </summary>
        private bool SchemaContainsLink(SchemaData sd)
        {
            bool schemaContainsLinks = false;

            SchemaFieldsData sfData =
                client.ReadSchemaFields(sd.Id, false, null);

            ItemFieldDefinitionData[] fields = sfData.Fields;

            if (null != fields)
            {
                for (int i = 0; i < fields.Length; i++)
                {
                    if (FieldContainsLink(fields[i]))
                    {
                        schemaContainsLinks = true;
                        break;
                    }
                }
            }

            ItemFieldDefinitionData[] mdFields = sfData.MetadataFields;

            if (null != mdFields && !schemaContainsLinks)
            {
                for (int i = 0; i < mdFields.Length; i++)
                {
                    if (FieldContainsLink(mdFields[i]))
                    {
                        schemaContainsLinks = true;
                        break;
                    }
                }
            }

            return schemaContainsLinks;
        }

        /// <summary>
        /// This helper method returns true iff a given field is a component
        /// link, multimedia link or an embedded schema that contains such a
        /// link.
        /// </summary>
        private bool FieldContainsLink(ItemFieldDefinitionData field)
        {
            bool containsLink = false;

            if ((field is ComponentLinkFieldDefinitionData &&
                 field.MinOccurs > 0) ||
                (field is MultimediaLinkFieldDefinitionData &&
                 field.MinOccurs > 0))
            {
                containsLink = true;
            }
            else if (field is EmbeddedSchemaFieldDefinitionData &&
                     field.MinOccurs > 0)
            {
                EmbeddedSchemaFieldDefinitionData currFieldAsES =
                    (EmbeddedSchemaFieldDefinitionData)field;

                string embSchemaId = currFieldAsES.EmbeddedSchema.IdRef;

                SchemaData embSchemaData =
                    (SchemaData)client.Read(embSchemaId, new ReadOptions());

                containsLink = SchemaContainsLink(embSchemaData);
            }
            else
            {
                // Do nothing
            }

            return containsLink;
        }
    }
}
