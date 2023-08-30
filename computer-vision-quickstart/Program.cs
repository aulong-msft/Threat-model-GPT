using Azure;
using Azure.AI.OpenAI;
using DotNetEnv;
//using Microsoft.Azure.Management.Security;
//using Microsoft.Azure.Management.Security.Models;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models;
using Octokit;
using Azure.Identity;


namespace ThreatModelGPT
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // Load environment variables from .env file
            Env.Load();
            
            // Assign values from environment variables
            string openAiApiKey = Env.GetString("OPENAI_API_KEY");
            string openAiApiendpoint = Env.GetString("OPENAI_API_ENDPOINT");
            string computerVisionApiKey = Env.GetString("COMPUTER_VISION_API_KEY");
            string computerVisionApiEndpoint = Env.GetString("COMPUTER_VISION_API_ENDPOINT");
            string imageFilePath = Env.GetString("IMAGE_FILEPATH");
            string githubUsername = Env.GetString("GITHUB_USERNAME");
            string githubPersonalAccessToken = Env.GetString("GITHUB_PERSONAL_ACCESS_TOKEN");
  
            // Create a computer vision client to obtain text from a provided image
            ComputerVisionClient client = Authenticate(computerVisionApiEndpoint, computerVisionApiKey);

            // Extract text (OCR) from the provided local image file
            var extractedText = await ReadLocalImage(client, imageFilePath);

            Console.WriteLine("Extracted Text from Image:");
            Console.WriteLine(extractedText);

            // Use OpenAI API to generate intelligible keywords from the extracted text
            var listOfServices = await GenerateListOfServices(extractedText, openAiApiKey, openAiApiendpoint);

            // Use OpenAI API to generate recommendations from the extracted text
            string concatenatedString = string.Join(",", listOfServices); // Using a space as delimiter
            var recommendations = await GenerateListOfSecurityRecommendations(concatenatedString, openAiApiKey, openAiApiendpoint);

            List<string> securityBaselines = await GetSecurityBaselinesAsync(concatenatedString, githubUsername, githubPersonalAccessToken);
            
            foreach (string baseline in securityBaselines)
            {
                if (!string.IsNullOrWhiteSpace(baseline)) // Check if the baseline is not null, empty, or whitespace
                {
                    Console.WriteLine("SECURITY BASELINE FOR:" + baseline);
                }
            }
        }

        public static ComputerVisionClient Authenticate(string endpoint, string key)
        {
            ComputerVisionClient client =
                new ComputerVisionClient(new ApiKeyServiceClientCredentials(key))
                { Endpoint = endpoint };
            return client;
        }

        public static async Task<string> ReadLocalImage(ComputerVisionClient client, string imagePath)
        {
            Console.WriteLine("----------------------------------------------------------");
            Console.WriteLine("READ LOCAL IMAGE");
            Console.WriteLine();

            // Read image data
            byte[] imageData = File.ReadAllBytes(imagePath);

            // Read text from image data
            var textHeaders = await client.ReadInStreamAsync(new MemoryStream(imageData));
            
            // After the request, get the operation location (operation ID)
            string operationLocation = textHeaders.OperationLocation;
            Thread.Sleep(2000);

            // Retrieve the URI where the extracted text will be stored from the Operation-Location header.
            // We only need the ID and not the full URL
            const int numberOfCharsInOperationId = 36;
            string operationId = operationLocation.Substring(operationLocation.Length - numberOfCharsInOperationId);

            // Extract the text
            ReadOperationResult results;
            Console.WriteLine($"Extracting text from local image {Path.GetFileName(imagePath)}...");
            Console.WriteLine();

            do
            {
                results = await client.GetReadResultAsync(Guid.Parse(operationId));
            }
            while (results.Status == OperationStatusCodes.Running || results.Status == OperationStatusCodes.NotStarted);

            // Store and return the extracted text
            var extractedText = "";
            Console.WriteLine();
            var textUrlFileResults = results.AnalyzeResult.ReadResults;
            
            foreach (ReadResult page in textUrlFileResults)
            {
                foreach (Line line in page.Lines)
                {
                    extractedText += line.Text + "\n";
                }
            }
            return extractedText;
        }

       public static async Task<List<string>> GenerateListOfServices(string text, string apiKey, string apiEndpoint)
        {
            string engine = "text-davinci-003";
            List<string> recommendations = new List<string>(); 
            string prompt = $"Prompt 1: You are a Microsoft Azure security engineer doing threat model analysis to identify and mitigate risk. Given the following text:\n{text}\n please find the relevant Azure Services and print them out. \n";

            OpenAIClient client = new OpenAIClient(new Uri(apiEndpoint), new AzureKeyCredential(apiKey));

            // Prompt tuning parameters
            Console.Write($"Input: {prompt}");
            CompletionsOptions completionsOptions = new CompletionsOptions();
            completionsOptions.Prompts.Add(prompt);
            completionsOptions.MaxTokens = 500;
            completionsOptions.Temperature = 0.2f;

            Response<Completions> completionsResponse = client.GetCompletions(engine, completionsOptions);
            string completion = completionsResponse.Value.Choices[0].Text;
            Console.WriteLine($"Chatbot: {completion}");
            recommendations.Add(completion); // Add the completion to the list

            return recommendations; 
        }
       public static async Task<List<string>> GenerateListOfSecurityRecommendations(string text, string apiKey, string apiEndpoint)
        {
            string engine = "audreysmodel"; // gpt 3.5 turbo
            List<string> recommendations = new List<string>();
            string prompt =
                "Prompt 2:\n" +
                "As a Microsoft Azure security engineer specializing in threat model analysis and risk mitigation, you have been tasked with evaluating the security posture of various Azure services:\n" +
                $"{text}\n" +
                "Your objective is to identify service-specific security recommendations by leveraging Azure Security Basline documentation and Microsoft docs. Explore the https://learn.microsoft.com/en-us/security/benchmark/azure/ and related documentation to find tailored security advice for each service and print your sources. Avoid repetitive suggestions and provide at least three distinct recommendations for each service. Customize the recommendations to address the unique security considerations associated with each service.\n";


            OpenAIClient client = new OpenAIClient(new Uri(apiEndpoint), new AzureKeyCredential(apiKey));

            // Prompt tuning parameters
            Console.Write($"Input: {prompt}");
            CompletionsOptions completionsOptions = new CompletionsOptions();
            completionsOptions.Prompts.Add(prompt);
            completionsOptions.MaxTokens = 2000;
            completionsOptions.Temperature = 0.7f;
            completionsOptions.NucleusSamplingFactor = 0.7f;

            Response<Completions> completionsResponse = client.GetCompletions(engine, completionsOptions);
            string completion = completionsResponse.Value.Choices[0].Text;
            Console.WriteLine($"Chatbot: {completion}");
            recommendations.Add(completion); // Add the completion to the list

            return recommendations;
        }
        static async Task<List<string>> GetSecurityBaselinesAsync(string services, string username, string personalAccessToken)
        {
            string owner = "MicrosoftDocs";
            string repo = "SecurityBenchmarks";
            string path = "Azure Offer Security Baselines/3.0";
            string path2 = "Microsoft Cloud Security Benchmark/";
            string path3 = "Azure Security Benchmark/3.0";

            var client = new GitHubClient(new ProductHeaderValue("MyApp"));
            var basicAuth = new Credentials(username, personalAccessToken); 
            client.Credentials = basicAuth;

            List<string> securityBaselines = new List<string>();
            string[] individualServices = services.Split(',').Select(s => s.Trim()).ToArray();

            // Find a security baseline for each recommendation from the OpenAI API
            foreach (string individualService in individualServices)
            {
                try
                {
                    string trimmedService = individualService.Trim();
                    string content = await GetFileContentAsync(client, owner, repo, path, trimmedService);
                    securityBaselines.Add(content);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to fetch data for service: {individualService}, Error: {ex.Message}");
                }
            }

            // Find the security baseline for the entire Azure Security Benchmark
            try
            {
                string content = await GetFileContentAsync(client, owner, repo, path3, "Azure Security Benchmark");
                securityBaselines.Add(content);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to fetch data for Azure Security Benchmark, Error: {ex.Message}");
            }

            // Find the security baseline for the entire Microsoft Cloud Security Benchmark
            try
            {
                string content = await GetFileContentAsync(client, owner, repo, path2, "Microsoft_cloud_security_benchmark");
                securityBaselines.Add(content);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to fetch data for Microsoft Cloud Security Benchmark, Error: {ex.Message}");
            }

            return securityBaselines;
        }
                
        static async Task<string> GetFileContentAsync(GitHubClient client, string owner, string repo, string path, string service)
        {
            var contents = await client.Repository.Content.GetAllContentsByRef(owner, repo, path, "master");
                        
            foreach (var content in contents)
            {
                // Replace spaces with hyphens to match filename format
                string formattedService = service.Replace(" ", "-").Replace(".", "").ToLower();
                
                if (content.Name.IndexOf(formattedService, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // Construct the URL to the baseline file
                    string baselineUrl = $" {service}: https://github.com/{owner}/{repo}/blob/master/{content.Path}";

                    return baselineUrl;
                }
            }

            // return empty string if the list isnt populated
           return "";

 /*       static void DownloadDataAndBuildDatabase()
        {
            if (!Directory.Exists(PERSIST_DIRECTORY))
            {
                var credential = new DefaultAzureCredential();
                var client = new SecurityCenterClient(new Uri("https://management.azure.com"), new DefaultAzureCredential());

                var assessmentsMetadataList = client.AssessmentsMetadata.List();

                var documents = new List<Document>();
                foreach (var assessmentsMetadata in assessmentsMetadataList)
                {
                    // Create a new Document
                    var newDocument = new Document(assessmentsMetadata.DisplayName, new Dictionary<string, object>());
                    documents.Add(newDocument);
                }

                var embeddings = new Embeddings(); // You need to initialize your embedding function

                var database = Chroma.FromDocuments(documents, embeddings, PERSIST_DIRECTORY);
                database.Persist();
            }
            else
            {
                var embeddings = new Embeddings(); // You need to initialize your embedding function

                var database = new Chroma(embeddings, PERSIST_DIRECTORY);
            }
        }
*/




        }
    }
}     