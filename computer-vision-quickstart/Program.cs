
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models;
using Azure;
using Azure.AI.OpenAI;
using static System.Environment;
using System;
using System.Collections.Generic;
using DotNetEnv;
using System.Threading.Tasks;
using Azure.AI.OpenAI;

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
  

            Console.WriteLine("Azure Cognitive Services Computer Vision - .NET quickstart example");
            ComputerVisionClient client = Authenticate(computerVisionApiEndpoint, computerVisionApiKey);

            // Extract text (OCR) from the provided local image file
            var extractedText = await ReadLocalImage(client, imageFilePath);

            Console.WriteLine("Extracted Text from Image:");
            Console.WriteLine(extractedText);

            // Use OpenAI API to generate recommendations from the extracted text
            var recommendations = await GenerateOpenAIRecommendations(extractedText, openAiApiKey, openAiApiendpoint);

          /*  Console.WriteLine("Recommended Actions:");
            foreach (var recommendation in recommendations)
            {
                Console.WriteLine(recommendation);
            }*/
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

       public static async Task<List<string>> GenerateOpenAIRecommendations(string text, string apiKey, string apiEndpoint)
        {
            string engine = "text-davinci-003";
            List<string> recommendations = new List<string>(); // Initialize the list
            List<string> prompts = new(){
                $"Prompt 1: You are a Microsoft security engineer doing threat model analysis to identity and mitigate risk. Given the following text:\n{text}\n please find the keywords relevant to a security engineer and print them out. \n",
                $"Prompt 2: Enumerate the results from the previous prompt. \n"

            };

            OpenAIClient client = new OpenAIClient(new Uri(apiEndpoint), new AzureKeyCredential(apiKey));

            foreach (string prompt in prompts)
            {
                Console.Write($"Input: {prompt}");
                CompletionsOptions completionsOptions = new CompletionsOptions();
                completionsOptions.Prompts.Add(prompt);
                completionsOptions.MaxTokens = 500;
                completionsOptions.Temperature = 0.2f;

                Response<Completions> completionsResponse = client.GetCompletions(engine, completionsOptions);
                string completion = completionsResponse.Value.Choices[0].Text;
                Console.WriteLine($"Chatbot: {completion}");
                recommendations.Add(completion); // Add the completion to the list

            }
        //    Response<Completions> completionsResponse = await client.GetCompletionsAsync(engine, completionsOptions);

           // string completion = completionsResponse.Value.Choices[0].Text;
           // Console.WriteLine($"Chatbot: {completion}");

           // recommendations.Add(completion); // Add the completion to the list

            return recommendations; // Return the list
        }
    }
}
