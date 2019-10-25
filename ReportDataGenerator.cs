using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.IO;
using System.Net;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Authentication;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core;
using System.Collections.Generic;
using System.Linq;

namespace FunctionGenerateReportData
{
    public static class ReportDataGenerator
    {

        private static readonly string BLOB_STORAGE_CONNECTION_STRING = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
        static string downloadUsageCNByMonthUrlTemplate = string.Format(@"{0}?month={{1}}&type={{2}}&fmt={{3}}", Environment.GetEnvironmentVariable("UsageReportAPIUrlCN"));
        static string downloadUsageGlobalByMonthUrlTemplate = Environment.GetEnvironmentVariable("UsageReportAPIUrlGlobal");



        [FunctionName("ReportDataGenerator")]
        public static void Run([TimerTrigger("0 */60 * * * *", RunOnStartup = true)]TimerInfo myTimer, ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            try
            {


                string clientIDGlobal = Environment.GetEnvironmentVariable("ClientIdGlobal");
                string clientSecretGlobal = Environment.GetEnvironmentVariable("ClientSecretGlobal");
                string tenantIdGlobal = Environment.GetEnvironmentVariable("TenantIdGlobal");

                var credsGlobal = new AzureCredentialsFactory().FromServicePrincipal(clientIDGlobal, clientSecretGlobal, tenantIdGlobal, AzureEnvironment.AzureGlobalCloud);

                IList<AzureSubScription> azureSubScriptionsGlobal = GetSubScriptionsWithResouceGroup(credsGlobal);

                //=================================================================
                // Authenticate
                // AzureCredentials credentials = SdkContext.AzureCredentialsFactory.FromFile(Environment.GetEnvironmentVariable("AZURE_AUTH_LOCATION"));



                // Add a message to the output collection.

                var storageAccount = CloudStorageAccount.Parse(BLOB_STORAGE_CONNECTION_STRING);
                var blobClient = storageAccount.CreateCloudBlobClient();
                string dataContainerName = Environment.GetEnvironmentVariable("DATA_CONTAINER_NAME");
                var container = blobClient.GetContainerReference(dataContainerName);

                var enrollmentNumberCN = Environment.GetEnvironmentVariable("EnrollmentNumberCN");
                var accessKeyCN = Environment.GetEnvironmentVariable("AccessKeyCN");
                var enrollmentNumberGlobal = Environment.GetEnvironmentVariable("EnrollmentNumberGlobal");
                var accessKeyGlobal = Environment.GetEnvironmentVariable("AccessKeyGlobal");
                var fieldMapping = Environment.GetEnvironmentVariable("FieldMapping");

                var fieldArray = fieldMapping.Split(',');
                string[][] fieldMappingArray = new string[fieldArray.Length][];
                for (int arrayLoop = 0; arrayLoop < fieldArray.Length; arrayLoop++)
                {
                    fieldMappingArray[arrayLoop] = fieldArray[arrayLoop].Split('|');
                }

                string sharedTitle = "";
                for (int arrayLoop = 0; arrayLoop < fieldArray.Length; arrayLoop++)
                {
                    sharedTitle += fieldMappingArray[arrayLoop][0] + ",";
                }

                if (sharedTitle.EndsWith(","))
                {
                    sharedTitle = sharedTitle.Substring(0, sharedTitle.Length - 1);
                }

                string fullTitle = "ProjectName,Source," + sharedTitle;

                StringBuilder AllData = new StringBuilder(fullTitle);
                AllData.AppendLine();



                DateTime startDate = new DateTime(2018, 12, 1);

                for (DateTime dateLoop = DateTime.Now; dateLoop > startDate; dateLoop = dateLoop.AddMonths(-1))
                {
                    using (var outputGlobal = GetEnrollmentUsageByMonthStreamGlobal(dateLoop, enrollmentNumberGlobal, accessKeyGlobal))
                    {
                        var monthDataGlobal = CustomColumnGlobal(outputGlobal, sharedTitle, fieldMappingArray, "Global", azureSubScriptionsGlobal);
                        AllData.Append(monthDataGlobal);

                        StringBuilder dataToUploadGlobal = new StringBuilder(fullTitle);
                        dataToUploadGlobal.AppendLine();
                        dataToUploadGlobal.Append(monthDataGlobal);

                        string blobName = dateLoop.ToString("yyyyMM") + "BillingGlobal.csv";

                        using (MemoryStream stream = new MemoryStream())
                        {
                            SaveCSVToBlob(container, stream, dataToUploadGlobal.ToString(), blobName);

                        }



                    }

                }


                string clientIDCN = Environment.GetEnvironmentVariable("ClientIdCN");
                string clientSecretCN = Environment.GetEnvironmentVariable("ClientSecretCN");
                string tenantIdCN = Environment.GetEnvironmentVariable("TenantIdCN");

                var credsCN = new AzureCredentialsFactory().FromServicePrincipal(clientIDCN, clientSecretCN, tenantIdCN, AzureEnvironment.AzureChinaCloud);

                IList<AzureSubScription> azureSubScriptionsCN = GetSubScriptionsWithResouceGroup(credsCN);



                for (DateTime dateLoop = DateTime.Now; dateLoop > startDate; dateLoop = dateLoop.AddMonths(-1))
                {


                    using (var outputCN = GetEnrollmentUsageByMonthStream(dateLoop, "Detail", enrollmentNumberCN, accessKeyCN, "csv"))
                    {
                        var monthDataCN = CustomColumnGlobal(outputCN, sharedTitle, fieldMappingArray, "CN", azureSubScriptionsCN);
                        AllData.Append(monthDataCN);

                        StringBuilder dataToUploadCN = new StringBuilder(fullTitle);
                        dataToUploadCN.AppendLine();
                        dataToUploadCN.Append(monthDataCN);

                        string blobNameCN = dateLoop.ToString("yyyyMM") + "BillingCN.csv";

                        using (MemoryStream stream = new MemoryStream())
                        {
                            SaveCSVToBlob(container, stream, dataToUploadCN.ToString(), blobNameCN);
                        }

                    }

                }

                string blobNameAll = "BillingDataAll.csv";

                using (MemoryStream stream = new MemoryStream())
                {
                    SaveCSVToBlob(container, stream, AllData.ToString(), blobNameAll);
                }


            }
            catch (Exception ex)
            {
                log.LogInformation(ex.Message);
                throw;
            }
        }

        private static IList<AzureSubScription> GetSubScriptionsWithResouceGroup(AzureCredentials creds)
        {
            IList<AzureSubScription> azureSubScriptions = new List<AzureSubScription>();

            var azure = Azure
                .Configure()
                .WithLogLevel(HttpLoggingDelegatingHandler.Level.Basic)
                .Authenticate(creds);

            var subscriptions = azure.Subscriptions.List();

            foreach (var subscription in subscriptions)
            {
                AzureSubScription azureSubScription = new AzureSubScription
                {
                    SubScriptionID = subscription.SubscriptionId,
                    SubScriptionName = subscription.DisplayName
                };
                var azureWithSubscription = azure.WithSubscription(azureSubScription.SubScriptionID);

                IList<AzureResourceGroup> azureResourceGroups = new List<AzureResourceGroup>();
                var resourceGroups = azureWithSubscription.ResourceGroups.List();
                foreach (var group in resourceGroups)
                {
                    var tags = group.Tags;
                    var projectName = azureSubScription.SubScriptionName;
                    if (tags != null && tags.ContainsKey("ProjectName"))
                    {
                        tags.TryGetValue("ProjectName", out projectName);
                    }
                    AzureResourceGroup azureResourceGroup = new AzureResourceGroup
                    {
                        Name = group.Name,
                        ProjectName = projectName
                    };
                    azureResourceGroups.Add(azureResourceGroup);
                }
                azureSubScription.ResourceGroups = azureResourceGroups;
                azureSubScriptions.Add(azureSubScription);
            }

            return azureSubScriptions;
        }

        private static void SaveCSVToBlob(CloudBlobContainer container, MemoryStream stream, string dataToUploadGlobal, string blobName)
        {
            byte[] byteContent = new byte[0];

            using (var writer = new StreamWriter(stream, new UTF8Encoding(true)))
            {
                writer.Write(dataToUploadGlobal);
                writer.Flush();
                byteContent = stream.ToArray();
            }
            stream.Close();
            stream.Dispose();

            var blockBlobGlobalbom = container.GetBlockBlobReference(blobName);

            //AccessCondition ac = new AccessCondition();
            //BlobRequestOptions timeoutRequestOptions = new BlobRequestOptions()
            //{
            //    // Each REST operation will timeout after 5 seconds.
            //    ServerTimeout = TimeSpan.FromMinutes(60),

            //    // Allot 30 seconds for this API call, including retries
            //    MaximumExecutionTime = TimeSpan.FromMinutes(60)
            //};
            //OperationContext oc = new OperationContext();

            blockBlobGlobalbom.UploadFromByteArrayAsync(byteContent, 0, byteContent.Length);
        }



        public static StringBuilder CustomColumnGlobal(Stream output, string sharedTitle, string[][] fieldMappingArray, string source, IList<AzureSubScription> azureSubScriptions)
        {
            int[] usedTitleLocationArray = new int[fieldMappingArray.Length];
            int[] resourceGroupLocationArray = new int[2];
            int[] subscriptionNameLocationArray = new int[2];
            int[] subscriptionIDLocationArray = new int[2];
            int costLocationGlobal = 0;
            string exchangeRateConfig = Environment.GetEnvironmentVariable("ExchangeRate");
            double exchangeRate = Convert.ToDouble(exchangeRateConfig);

            StringBuilder newLines = new StringBuilder();

            using (StreamReader sr = new StreamReader(output))
            {
                int loopLine = 0;
                while (sr.Peek() >= 0)
                {
                    if (loopLine < 2)
                    {
                        //NoUse Title ,Skipped
                        sr.ReadLine();
                        loopLine++;

                    }
                    else if (loopLine == 2)
                    {
                        //Real Title
                        var titleLine = sr.ReadLine();
                        var oldTitleArray = titleLine.Split(',');
                        if (source == "Global")
                        {
                            for (int fieldMappingArrayLoop = 0; fieldMappingArrayLoop < fieldMappingArray.Length; fieldMappingArrayLoop++)
                            {
                                for (int titleArrayLoop = 0; titleArrayLoop < oldTitleArray.Length; titleArrayLoop++)
                                {
                                    if (fieldMappingArray[fieldMappingArrayLoop][0] == oldTitleArray[titleArrayLoop])
                                    {
                                        usedTitleLocationArray[fieldMappingArrayLoop] = titleArrayLoop;
                                        if (fieldMappingArray[fieldMappingArrayLoop][0] == "ResourceGroup")
                                        {
                                            resourceGroupLocationArray[0] = titleArrayLoop;
                                        }
                                        if (fieldMappingArray[fieldMappingArrayLoop][0] == "SubscriptionName")
                                        {
                                            subscriptionNameLocationArray[0] = titleArrayLoop;
                                        }
                                        if (fieldMappingArray[fieldMappingArrayLoop][0] == "SubscriptionGuid")
                                        {
                                            subscriptionIDLocationArray[0] = titleArrayLoop;
                                        }
                                        if (fieldMappingArray[fieldMappingArrayLoop][0] == "Cost")
                                        {
                                            costLocationGlobal = titleArrayLoop;
                                        }
                                    }
                                }
                            }
                        }
                        if (source == "CN")
                        {
                            string pattern = @"\((?<CNTitle>.*)\)";//匹配模式
                            Regex rex = new Regex(pattern, RegexOptions.IgnoreCase);

                            for (int fieldMappingArrayLoop = 0; fieldMappingArrayLoop < fieldMappingArray.Length; fieldMappingArrayLoop++)
                            {
                                for (int titleArrayLoop = 0; titleArrayLoop < oldTitleArray.Length; titleArrayLoop++)
                                {
                                    //Global match CN field title
                                    if (fieldMappingArray[fieldMappingArrayLoop][1] == rex.Match(oldTitleArray[titleArrayLoop]).Groups["CNTitle"].ToString())
                                    {
                                        usedTitleLocationArray[fieldMappingArrayLoop] = titleArrayLoop;
                                        if (fieldMappingArray[fieldMappingArrayLoop][1] == "Resource Group")
                                        {
                                            resourceGroupLocationArray[1] = titleArrayLoop;
                                        }
                                        if (fieldMappingArray[fieldMappingArrayLoop][1] == "Subscription Name")
                                        {
                                            subscriptionNameLocationArray[1] = titleArrayLoop;
                                        }
                                        if (fieldMappingArray[fieldMappingArrayLoop][1] == "SubscriptionGuid")
                                        {
                                            subscriptionIDLocationArray[1] = titleArrayLoop;
                                        }
                                    }
                                }
                            }
                        }

                        loopLine++;

                    }
                    else
                    {
                        //data ,add two fields
                        var dataLine = sr.ReadLine();
                        var orgDataline = dataLine;
                        if (!string.IsNullOrEmpty(dataLine))
                        {
                            Regex CSVParser = new Regex(",(?=(?:[^\"]*\"[^\"]*\")*(?![^\"]*\"))");

                            //string patternRemove = "{[^{}]+}";//内层大括号匹配模式
                            //Regex rex = new Regex(patternRemove, RegexOptions.IgnoreCase);
                            //var contentGroupsToRemove = CSVParser.Matches(dataLine);
                            //foreach (var content in contentGroupsToRemove)
                            //{
                            //    var contentToRemove = content.ToString();
                            //    if (!string.IsNullOrEmpty(contentToRemove))
                            //    {
                            //        dataLine = dataLine.Replace(contentToRemove, "JsonString");
                            //    }
                            //}

                            //string patternRemoveOuter = "{[^}]+}";//外层大括号匹配模式
                            //Regex rexOuter = new Regex(patternRemoveOuter, RegexOptions.IgnoreCase);
                            //var contentGroupsToRemoveOuter = rexOuter.Matches(dataLine);
                            //foreach (var content in contentGroupsToRemoveOuter)
                            //{
                            //    var contentToRemove = content.ToString();
                            //    if (!string.IsNullOrEmpty(contentToRemove))
                            //    {
                            //        dataLine = dataLine.Replace(contentToRemove, "JsonString");
                            //    }
                            //}

                            var fields = CSVParser.Split(dataLine);
                            StringBuilder newDataLineBuilder = new StringBuilder();
                            for (int titleArrayLoop = 0; titleArrayLoop < usedTitleLocationArray.Length; titleArrayLoop++)
                            {
                                if (source == "Global" && titleArrayLoop == costLocationGlobal)
                                {
                                    var cost = fields[costLocationGlobal];
                                    double.TryParse(cost, out double RMBCost);
                                    RMBCost = RMBCost * exchangeRate;

                                    newDataLineBuilder.Append(RMBCost);
                                }
                                else
                                {
                                    try
                                    {
                                        newDataLineBuilder.Append(fields[usedTitleLocationArray[titleArrayLoop]]);
                                    }
                                    catch (Exception ex)
                                    {
                                        throw;
                                    }
                                }

                                newDataLineBuilder.Append(",");
                            }
                            var newDataLine = newDataLineBuilder.ToString();

                            if (newDataLine.EndsWith(","))
                            {
                                newDataLine = newDataLine.Substring(0, newDataLine.Length - 1);
                            }

                            string projectName = "";
                            string subscriptionID = "";
                            string subscriptionName = "";
                            string resourceGroup = "";
                            //TODO:Field used project Name; Data Field
                            if (source == "Global")
                            {

                                subscriptionName = fields[subscriptionNameLocationArray[0]];
                                subscriptionID = fields[subscriptionIDLocationArray[0]];
                                resourceGroup = fields[resourceGroupLocationArray[0]];
                            }

                            if (source == "CN")
                            {

                                subscriptionName = fields[subscriptionNameLocationArray[1]];
                                subscriptionID = fields[subscriptionIDLocationArray[1]];
                                resourceGroup = fields[resourceGroupLocationArray[1]];
                            }

                            var groups = (from azureSubScription in azureSubScriptions
                                          where azureSubScription.SubScriptionID == subscriptionID
                                          select azureSubScription.ResourceGroups).SingleOrDefault();

                            if (groups != null)
                            {

                                projectName = (from rgroup in groups
                                               where rgroup.Name == resourceGroup
                                               select rgroup.ProjectName).SingleOrDefault();
                            }
                            if (string.IsNullOrEmpty(projectName))
                            {
                                projectName = subscriptionName;
                            }

                            if (projectName == "0")
                            {
                                var olddata = orgDataline;
                                var data = newDataLine;
                            }
                            newLines.Append(projectName);
                            newLines.Append(",");
                            newLines.Append(source);
                            newLines.Append(",");
                            newLines.Append(newDataLine);
                            newLines.AppendLine();
                        }
                    }


                }
            }

            return newLines;

        }

        public static Stream GetEnrollmentUsageByMonthStreamGlobal(DateTime month, string enrollmentNumber, string jwt)
        {
            string url = string.Format(downloadUsageGlobalByMonthUrlTemplate, enrollmentNumber, month.ToString("yyyyMM"));
            Stream response = GetResponseStream(url, jwt);
            return response;
        }
        public static Stream GetEnrollmentUsageByMonthStream(DateTime month, string type, string enrollmentNumber, string jwt, string format)
        {
            string url = string.Format(downloadUsageCNByMonthUrlTemplate, enrollmentNumber, month, type, format);
            Stream response = GetResponseStream(url, jwt);
            return response;
        }



        private static Stream GetResponseStream(string url, string jwt)
        {
            WebRequest request = WebRequest.Create(url);

            //keep request openning for 5 minutes. the socket will close either file is downloaded or socket opening for longer than 5 minutes
            request.Timeout = 1000 * 60 * 5;
            if (!string.IsNullOrEmpty(jwt))
            {
                AddHeaders(request, jwt);
            }
            Stream retStream = null;
            try
            {
                HttpWebResponse response = null;
                try
                {
                    response = (HttpWebResponse)request.GetResponse();
                }
                catch (WebException ex)
                {
                    response = (HttpWebResponse)ex.Response;
                }

                retStream = response.GetResponseStream();

            }
            catch (Exception ex)
            {
                retStream = null;
            }
            return retStream;
        }


        private static void AddHeaders(WebRequest request, string jwt)
        {
            string bearerTokenHeader = BearerToken.FromJwt(jwt).BearerTokenHeader;
            request.Headers.Add("authorization", bearerTokenHeader);
            request.Headers.Add("api-version", "1.0");
        }


    }

    internal class BearerToken
    {
        private BearerToken() { }

        public static BearerToken Parse(string tokenHeader)
        {
            if (!tokenHeader.StartsWith("bearer", StringComparison.InvariantCultureIgnoreCase) || tokenHeader.Length < 8) //meaning the string after "bearer " is empty
            {
                throw new InvalidOperationException("not a valid bearer token");
            }

            return new BearerToken() { Token = tokenHeader.Substring(7), BearerTokenHeader = tokenHeader };//"bearer "

        }

        public static BearerToken FromJwt(string jwt)
        {
            string bearerToken = string.Concat("bearer ", jwt);
            return Parse(bearerToken);
        }

        public string Token
        {
            get;
            private set;
        }

        public string BearerTokenHeader
        {
            get;
            private set;
        }

    }

}
