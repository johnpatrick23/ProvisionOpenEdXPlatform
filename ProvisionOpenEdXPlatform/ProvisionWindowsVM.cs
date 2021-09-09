using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.Azure.Management.ResourceManager.Fluent.Authentication;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core;
using Microsoft.Azure.Management.Network.Fluent;
using Microsoft.Azure.Management.Storage.Fluent;
using Microsoft.Azure.Management.Compute.Fluent;
using Microsoft.Azure.Management.Compute.Fluent.Models;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace ProvisionOpenEdXPlatform
{
    public static class ProvisionWindowsVM
    {
        [FunctionName("ProvisionWindowsVM")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

            ProvisioningModelWindows provisioningModelWindows = JsonConvert.DeserializeObject<ProvisioningModelWindows>(requestBody);

            if (string.IsNullOrEmpty(provisioningModelWindows.ClientId) ||
                string.IsNullOrEmpty(provisioningModelWindows.ClientSecret) ||
                string.IsNullOrEmpty(provisioningModelWindows.TenantId) ||
                string.IsNullOrEmpty(provisioningModelWindows.SubscriptionId) ||
                string.IsNullOrEmpty(provisioningModelWindows.ClustrerName) ||
                string.IsNullOrEmpty(provisioningModelWindows.ResourceGroupName) ||
                string.IsNullOrEmpty(provisioningModelWindows.MainVhdURL))
            {
                log.LogInformation($"{DateAndTime()} | Error |  Missing parameter | \n{requestBody}");
                return new BadRequestObjectResult(false);
            }
            else
            {
                try
                {
                    string resourceGroupName = provisioningModelWindows.ResourceGroupName;
                    string clusterName = provisioningModelWindows.ClustrerName;
                    string contactPerson = "j-patrick@cloudswyft.com";
                    string MainVhdURL = provisioningModelWindows.MainVhdURL;

                    ServicePrincipalLoginInformation principalLogIn = new ServicePrincipalLoginInformation();
                    principalLogIn.ClientId = provisioningModelWindows.ClientId;
                    principalLogIn.ClientSecret = provisioningModelWindows.ClientSecret;

                    AzureEnvironment environment = AzureEnvironment.AzureGlobalCloud;
                    AzureCredentials credentials = new AzureCredentials(principalLogIn, provisioningModelWindows.TenantId, environment);

                    IAzure _azureProd = Azure.Configure()
                          .WithLogLevel(HttpLoggingDelegatingHandler.Level.Basic)
                          .Authenticate(credentials)
                          .WithSubscription(provisioningModelWindows.SubscriptionId);

                    IResourceGroup resourceGroup = _azureProd.ResourceGroups.GetByName(resourceGroupName);
                    Region region = resourceGroup.Region;

                    log.LogInformation($"{DateAndTime()} | Detected | RG");

                    INetwork virtualNetwork = _azureProd.Networks.GetByResourceGroup("CS-TST-PAT", "CS-TST-PAT-vnet");

                    log.LogInformation($"{DateAndTime()} | Detected | VNET");

                    #region Create VM IP
                    IPublicIPAddress publicIpAddress = _azureProd.PublicIPAddresses.Define($"{clusterName}")
                       .WithRegion(region)
                       .WithExistingResourceGroup(resourceGroupName)
                       .WithDynamicIP()
                       .WithLeafDomainLabel(clusterName)
                       .WithTag("_contact_person", contactPerson)
                       .Create();

                    log.LogInformation($"{DateAndTime()} | Created | VM IP Address");
                    #endregion

                    #region NSG
                    INetworkSecurityGroup networkSecurityGroup = _azureProd.NetworkSecurityGroups.GetByResourceGroup("",$"{clusterName}-nsg");
                    #endregion

                    #region nic
                    INetworkInterface networkInterface = _azureProd.NetworkInterfaces.Define($"{clusterName}")
                        .WithRegion(region)
                        .WithExistingResourceGroup(resourceGroupName)
                        .WithExistingPrimaryNetwork(virtualNetwork)
                        .WithSubnet("default")
                        .WithPrimaryPrivateIPAddressDynamic()
                        .WithExistingPrimaryPublicIPAddress(publicIpAddress)
                        
                        .WithTag("_contact_person", contactPerson)
                        .Create();

                    log.LogInformation($"{DateAndTime()} | Created | Network Interface");
                    #endregion
                    
                    IStorageAccount storageAccount = _azureProd.StorageAccounts.GetByResourceGroup("CS-TST-PAT", "faresourcekiller");

                    #region vm
                    IVirtualMachine createVm = _azureProd.VirtualMachines.Define($"{clusterName}")
                        .WithRegion(region)
                        .WithExistingResourceGroup(resourceGroupName)
                        .WithExistingPrimaryNetworkInterface(networkInterface)
                        .WithStoredWindowsImage(MainVhdURL)
                        .WithAdminUsername("cloudswyft")
                        .WithAdminPassword("pr0v3byd01n6!")
                        .WithComputerName(clusterName)
                        //.DefineNewExtension("CreateUser").WithPublisher("").
                        .WithBootDiagnostics(storageAccount)
                        .WithSize(VirtualMachineSizeTypes.StandardB4ms)
                        .WithTag("_contact_person", contactPerson)
                        .Create();
                    #endregion

                    string url = "https://18b9b200-94f8-4b85-b006-cfe4178efa7c.webhook.sea.azure-automation.net/webhooks?token=hxlFi339vmhpy0y%2fykkInD20S0a5IEy3cNFwHHHifxE%3d";

                    GuestUserInfo guestUserInfo = new GuestUserInfo();
                    guestUserInfo.ClientId = provisioningModelWindows.ClientId;
                    guestUserInfo.ClientSecret = provisioningModelWindows.ClientSecret;
                    guestUserInfo.TenantId = provisioningModelWindows.TenantId;
                    guestUserInfo.SubscriptionId = provisioningModelWindows.SubscriptionId;
                    guestUserInfo.VMName = clusterName;
                    guestUserInfo.ResourceGroup = resourceGroupName;
                    guestUserInfo.Username = clusterName;
                    guestUserInfo.Password = "pr0v3byd01n6!";

                    Task<string> task = CreateGuestUserAsync(url, JsonConvert.SerializeObject(guestUserInfo));
                    task.Wait();

                    return new OkObjectResult(true);
                }
                catch (Exception e)
                {
                    log.LogInformation($"{DateAndTime()} | Error | {e.Message}");
                    return new BadRequestObjectResult(false);
                }
            }
        }

        public static async Task<string> CreateGuestUserAsync(string url, string data = null)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            HttpClient client = new HttpClient();
            client.BaseAddress = new Uri(url);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            HttpResponseMessage response = null;
            response = await client.PostAsync(client.BaseAddress, new StringContent(data));

            return await response.Content.ReadAsStringAsync();
        }

        private static string DateAndTime()
        {
            return $"{ DateTime.Now.ToShortDateString()} { DateTime.Now.ToShortTimeString()}";
        }
    }
}
