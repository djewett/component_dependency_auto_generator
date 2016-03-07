using System;
using System.Configuration;
using Tridion.ContentManager.CoreService.Client;

namespace CoreService.ComponentDependencies
{
    class ComponentCreatorTest
    {
        static void Main(string[] args)
        {
            string endpointName =
                ConfigurationManager.AppSettings["CoreServiceEndpoint"];

            if (String.IsNullOrEmpty(endpointName))
                throw new ConfigurationErrorsException(
                    "CoreServiceEndpoint missing from appSettings");

            SessionAwareCoreServiceClient client =
                new SessionAwareCoreServiceClient(endpointName);

            // Dummy image file path for multimedia components, hard-coded:
            string dummyImagePath = @"C:\TempFiles\dummy.jpg";

            ComponentCreator resolver =
                new ComponentCreator(client, dummyImagePath);

            // TODO: Update code to use a specific folder where schemas are located
            // (as opposed to a publication, which may be too general):
            // Test publication, hard-coded:
            const string publicationUri = "tcm:0-5-1";

            // Test folder, hard-coded:
            const string folderForComponents = "tcm:5-2199-2";

            resolver.Resolve(publicationUri, folderForComponents);

            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey();
        }
    }
}
