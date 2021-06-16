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

                    INetwork virtualNetwork = _azureProd.Networks.GetByResourceGroup("CS-PRD-LOP", "CS-PRD-LOP-vnet");

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
                    
                    IStorageAccount storageAccount = _azureProd.StorageAccounts.GetByResourceGroup("CS-VHDList", "csvmdisk");

                    #region vm
                    IVirtualMachine createVm = _azureProd.VirtualMachines.Define($"{clusterName}")
                        .WithRegion(region)
                        .WithExistingResourceGroup(resourceGroupName)
                        .WithExistingPrimaryNetworkInterface(networkInterface)
                        .WithStoredWindowsImage(MainVhdURL)
                        .WithAdminUsername("cloudswyft")
                        .WithAdminPassword("CloudSwyft2021!")
                        .WithComputerName(clusterName)
                        .WithBootDiagnostics(storageAccount)
                        .WithSize(VirtualMachineSizeTypes.StandardB4ms)
                        .WithTag("_contact_person", contactPerson)
                        .Create();
                    #endregion

                    return new OkObjectResult(true);
                }
                catch (Exception e)
                {
                    log.LogInformation($"{DateAndTime()} | Error | {e.Message}");
                    return new BadRequestObjectResult(false);
                }
            }
        }

        private static string DateAndTime()
        {
            return $"{ DateTime.Now.ToShortDateString()} { DateTime.Now.ToShortTimeString()}";
        }
    }
}
