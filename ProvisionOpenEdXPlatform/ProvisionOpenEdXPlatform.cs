using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Net.Mail;

using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.Compute.Fluent;
using Microsoft.Azure.Management.Compute.Fluent.Models;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core;
using Microsoft.Azure.Management.ResourceManager.Fluent.Authentication;
using Microsoft.Azure.Management.Network.Fluent;
using Microsoft.Azure.Management.Network.Fluent.Models;
using Microsoft.Azure.Management.TrafficManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core.ResourceActions;
using Microsoft.Azure.Management.TrafficManager.Fluent.TrafficManagerProfile.Definition;
using Microsoft.Azure.Management.Storage.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent;

namespace ProvisionOpenEdXPlatform
{
    public static class ProvisionOpenEdXPlatform
    {
        [FunctionName("ProvisionOpenEdXPlatform")]

        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation($"{DateAndTime()} | C# HTTP trigger function processed a request.");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                
            ProvisioningModel provisioningModel = JsonConvert.DeserializeObject<ProvisioningModel>(requestBody);
            
            if (string.IsNullOrEmpty(provisioningModel.ClientId) ||
                string.IsNullOrEmpty(provisioningModel.ClientSecret) ||
                string.IsNullOrEmpty(provisioningModel.TenantId) ||
                string.IsNullOrEmpty(provisioningModel.SubscriptionId) ||
                string.IsNullOrEmpty(provisioningModel.ClustrerName) ||
                string.IsNullOrEmpty(provisioningModel.ResourceGroupName) ||
                string.IsNullOrEmpty(provisioningModel.VhdURL))
            {
                log.LogInformation($"{DateAndTime()} | Error |  Missing parameter | \n{requestBody}");
                return new BadRequestObjectResult(false);
            }
            else 
            {
                try
                {
                    ServicePrincipalLoginInformation principalLogIn = new ServicePrincipalLoginInformation();
                    principalLogIn.ClientId = provisioningModel.ClientId;
                    principalLogIn.ClientSecret = provisioningModel.ClientSecret;

                    AzureEnvironment environment = AzureEnvironment.AzureGlobalCloud;
                    AzureCredentials credentials = new AzureCredentials(principalLogIn, provisioningModel.TenantId, environment);

                    IAzure _azureProd = Azure.Configure()
                          .WithLogLevel(HttpLoggingDelegatingHandler.Level.Basic)
                          .Authenticate(credentials)
                          .WithSubscription(provisioningModel.SubscriptionId);

                    string resourceGroupName = provisioningModel.ResourceGroupName;
                    string clusterName = provisioningModel.ClustrerName;
                    string vhdURL = provisioningModel.VhdURL;
                    string subnet = "default";
                    string username = provisioningModel.Username;
                    string password = provisioningModel.Password;

                    IResourceGroup resourceGroup = _azureProd.ResourceGroups.GetByName(resourceGroupName);
                    Region region = resourceGroup.Region;

                    #region comment

                    #region Create Virtual Network
                    INetwork virtualNetwork = _azureProd.Networks.Define($"{clusterName}-vnet")
                        .WithRegion(region)
                        .WithExistingResourceGroup(resourceGroupName)
                        .WithAddressSpace("10.0.0.0/16")
                        .DefineSubnet(subnet)
                            .WithAddressPrefix("10.0.0.0/24")
                            .Attach()
                        .Create();
                    //_azureProd.Networks.GetByResourceGroup(resourceGroupName, $"{clusterName}-vnet");
                    
                    #endregion

                    log.LogInformation($"{DateAndTime()} | Created | VNET");

                    #region Create VM IP
                    IPublicIPAddress publicIpAddress = _azureProd.PublicIPAddresses.Define($"{clusterName}-vm-ip")
                       .WithRegion(region)
                       .WithExistingResourceGroup(resourceGroupName)
                       .WithDynamicIP()
                       .WithLeafDomainLabel(clusterName)
                       .Create();
                    #endregion

                    log.LogInformation($"{DateAndTime()} | Created | VM IP Address");

                    #region NSG
                    INetworkSecurityGroup networkSecurityGroup = _azureProd.NetworkSecurityGroups.Define($"{clusterName}-nsg")
                        .WithRegion(region)
                        .WithExistingResourceGroup(resourceGroupName)
                        .DefineRule("ALLOW-SSH")
                            .AllowInbound()
                            .FromAnyAddress()
                            .FromAnyPort()
                            .ToAnyAddress()
                            .ToPort(22)
                            .WithProtocol(SecurityRuleProtocol.Tcp)
                            .WithPriority(100)
                            .WithDescription("Allow SSH")
                            .Attach()
                        .DefineRule("LMS")
                            .AllowInbound()
                            .FromAnyAddress()
                            .FromAnyPort()
                            .ToAnyAddress()
                            .ToPort(80)
                            .WithProtocol(SecurityRuleProtocol.Tcp)
                            .WithPriority(101)
                            .WithDescription("LMS")
                            .Attach()
                        .DefineRule("CMS")
                            .AllowInbound()
                            .FromAnyAddress()
                            .FromAnyPort()
                            .ToAnyAddress()
                            .ToPort(18010)
                            .WithProtocol(SecurityRuleProtocol.Tcp)
                            .WithPriority(102)
                            .WithDescription("CMS")
                            .Attach()
                        .Create();
                    #endregion

                    log.LogInformation($"{DateAndTime()} | Created | Network Security Group");

                    #region nic
                    INetworkInterface networkInterface = _azureProd.NetworkInterfaces.Define($"{clusterName}-nic")
                        .WithRegion(region)
                        .WithExistingResourceGroup(resourceGroupName)
                        .WithExistingPrimaryNetwork(virtualNetwork)
                        .WithSubnet(subnet)
                        .WithPrimaryPrivateIPAddressDynamic()
                        .WithExistingPrimaryPublicIPAddress(publicIpAddress)
                        .WithExistingNetworkSecurityGroup(networkSecurityGroup)
                        .Create();
                    #endregion

                    log.LogInformation($"{DateAndTime()} | Created | Network Interface");

                    IStorageAccount storageAccount = _azureProd.StorageAccounts.GetByResourceGroup(resourceGroupName, $"{clusterName}vhdsa");

                    #region vm
                    IVirtualMachine createVm = _azureProd.VirtualMachines.Define($"{clusterName}-jb")
                        .WithRegion(region)
                        .WithExistingResourceGroup(resourceGroupName)
                        .WithExistingPrimaryNetworkInterface(networkInterface)
                        .WithStoredLinuxImage(vhdURL)
                        .WithRootUsername(username)
                        .WithRootPassword(password)
                        .WithComputerName("cloudswyft")
                        .WithBootDiagnostics(storageAccount)
                        .WithSize(VirtualMachineSizeTypes.StandardD2sV3)
                        .Create();
                    #endregion

                    log.LogInformation($"{DateAndTime()} | Created | Main Virtual Machine");

                    #region LMS IP
                    IPublicIPAddress publicIPAddressLMS = _azureProd.PublicIPAddresses.Define($"{clusterName}-lms-ip")
                        .WithRegion(region)
                        .WithExistingResourceGroup(resourceGroupName)
                        .WithDynamicIP()
                        .WithLeafDomainLabel($"{clusterName}-lms-ip")
                        .Create();
                    #endregion

                    log.LogInformation($"{DateAndTime()} | Created | LMS Public IP Address");

                    #region CMS IP
                    IPublicIPAddress publicIPAddressCMS = _azureProd.PublicIPAddresses.Define($"{clusterName}-cms-ip")
                        .WithRegion(region)
                        .WithExistingResourceGroup(resourceGroupName)
                        .WithDynamicIP()
                        .WithLeafDomainLabel($"{clusterName}-cms-ip")
                        .Create();
                    #endregion

                    log.LogInformation($"{DateAndTime()} | Created | CMS Public IP Address");

                    #region LoadBalancer
                    ILoadBalancer loadBalancer = _azureProd.LoadBalancers.Define($"{clusterName}-lb")
                        .WithRegion(region)
                        .WithExistingResourceGroup(resourceGroupName)

                        .DefineLoadBalancingRule("LBRuleCMS")
                            .WithProtocol(TransportProtocol.Tcp)
                            .FromFrontend("CMS")
                            .FromFrontendPort(80)
                            .ToBackend($"{clusterName}-bepool")
                            .ToBackendPort(18010)
                            .WithProbe("tcpProbeCMS")
                            .WithFloatingIPDisabled()
                            .Attach()

                        .DefineLoadBalancingRule("LBRuleCMS_SSL")
                            .WithProtocol(TransportProtocol.Tcp)
                            .FromFrontend("CMS")
                            .FromFrontendPort(443)
                            .ToBackend($"{clusterName}-bepool")
                            .ToBackendPort(48010)
                            .WithProbe("tcpProbeCMSSSL")
                            .WithFloatingIPDisabled()
                            .Attach()

                        .DefineLoadBalancingRule("LBRuleLMS")
                            .WithProtocol(TransportProtocol.Tcp)
                            .FromFrontend("LMS")
                            .FromFrontendPort(80)
                            .ToBackend($"{clusterName}-bepool")
                            .ToBackendPort(80)
                            .WithProbe("tcpProbeLMS")
                            .WithFloatingIPDisabled()
                            .Attach()

                        .DefineLoadBalancingRule("LBRuleLMS_SSL")
                            .WithProtocol(TransportProtocol.Tcp)
                            .FromFrontend("LMS")
                            .FromFrontendPort(443)
                            .ToBackend($"{clusterName}-bepool")
                            .ToBackendPort(443)
                            .WithProbe("tcpProbeLMSSSL")
                            .WithFloatingIPDisabled()
                            .Attach()

                        .DefineBackend($"{clusterName}-bepool")
                            .WithExistingVirtualMachines(createVm)
                            .Attach()

                        .DefinePublicFrontend("LMS")
                            .WithExistingPublicIPAddress(publicIPAddressLMS)
                            .Attach()

                        .DefinePublicFrontend("CMS")
                            .WithExistingPublicIPAddress(publicIPAddressCMS)
                            .Attach()

                        .DefineHttpProbe("tcpProbeCMS")
                            .WithRequestPath("/heartbeat")
                            .WithPort(18010)
                            .WithIntervalInSeconds(5)
                            .WithNumberOfProbes(6)
                            .Attach()

                        .DefineTcpProbe("tcpProbeCMSSSL")
                            .WithPort(48010)
                            .WithIntervalInSeconds(5)
                            .WithNumberOfProbes(6)
                            .Attach()

                        .DefineHttpProbe("tcpProbeLMS")
                            .WithRequestPath("/heartbeat")
                            .WithPort(80)
                            .WithIntervalInSeconds(5)
                            .WithNumberOfProbes(6)
                            .Attach()

                        .DefineTcpProbe("tcpProbeLMSSSL")
                            .WithPort(443)
                            .WithIntervalInSeconds(5)
                            .WithNumberOfProbes(6)
                            .Attach()
                        .Create();
                    #endregion

                    log.LogInformation($"{DateAndTime()} | Created | Load Balancer");

                    #region tm
                    IWithEndpoint tmDefinitionLMS = _azureProd.TrafficManagerProfiles
                            .Define($"{clusterName}-lms-tm")
                            .WithExistingResourceGroup(resourceGroupName)
                            .WithLeafDomainLabel($"{clusterName}-lms-tm")
                            .WithPriorityBasedRouting();
                    ICreatable<ITrafficManagerProfile> tmCreatableLMS = null;

                    tmCreatableLMS = tmDefinitionLMS
                        .DefineExternalTargetEndpoint($"{clusterName}-lms-tm")
                            .ToFqdn(publicIPAddressLMS.Fqdn)
                            .FromRegion(Region.AsiaSouthEast)
                            .WithRoutingPriority(1)
                            .Attach();

                    ITrafficManagerProfile trafficManagerProfileLMS = tmCreatableLMS.Create();

                    log.LogInformation($"{DateAndTime()} | Created | LMS Traffic Manager");

                    IWithEndpoint tmDefinitionCMS = _azureProd.TrafficManagerProfiles
                            .Define($"{clusterName}-cms-tm")
                            .WithExistingResourceGroup(resourceGroupName)
                            .WithLeafDomainLabel($"{clusterName}-cms-tm")
                            .WithPriorityBasedRouting();
                    ICreatable<ITrafficManagerProfile> tmCreatableCMS = null;

                    tmCreatableCMS = tmDefinitionCMS
                        .DefineExternalTargetEndpoint($"{clusterName}-cms-tm")
                            .ToFqdn(publicIPAddressCMS.Fqdn)
                            .FromRegion(Region.AsiaSouthEast)
                            .WithRoutingPriority(1)
                            .Attach();

                    ITrafficManagerProfile trafficManagerProfileCMS = tmCreatableCMS.Create();

                    log.LogInformation($"{DateAndTime()} | Created | CMS Traffic Manager");

                    #endregion

                    #endregion

                    #region mysql
                    INetworkSecurityGroup networkSecurityGroupmysql = _azureProd.NetworkSecurityGroups.Define($"{clusterName}-mysql-nsg")
                        .WithRegion(region)
                        .WithExistingResourceGroup(resourceGroup)
                        .DefineRule("ALLOW-SSH")
                            .AllowInbound()
                            .FromAnyAddress()
                            .FromAnyPort()
                            .ToAnyAddress()
                            .ToPort(22)
                            .WithProtocol(SecurityRuleProtocol.Tcp)
                            .WithPriority(100)
                            .WithDescription("Allow SSH")
                            .Attach()
                        .DefineRule("mysql")
                            .AllowInbound()
                            .FromAnyAddress()
                            .FromAnyPort()
                            .ToAnyAddress()
                            .ToPort(3306)
                            .WithProtocol(SecurityRuleProtocol.Tcp)
                            .WithPriority(101)
                            .WithDescription("mysql")
                            .Attach()
                        .Create();

                    log.LogInformation($"{DateAndTime()} | Created | MySQL Network Security Group");

                    INetworkInterface networkInterfacemysql = _azureProd.NetworkInterfaces.Define($"{clusterName}-mysql-nic")
                        .WithRegion(region)
                        .WithExistingResourceGroup(resourceGroup)
                        .WithExistingPrimaryNetwork(virtualNetwork)
                        .WithSubnet(subnet)
                        .WithPrimaryPrivateIPAddressDynamic()
                        .WithExistingNetworkSecurityGroup(networkSecurityGroupmysql)
                        .Create();

                    log.LogInformation($"{DateAndTime()} | Created | MySQL Network Interface");

                    IVirtualMachine createVmmysql = _azureProd.VirtualMachines.Define($"{clusterName}-mysql")
                        .WithRegion(region)
                        .WithExistingResourceGroup(resourceGroup)
                        .WithExistingPrimaryNetworkInterface(networkInterfacemysql)
                        .WithStoredLinuxImage(vhdURL)
                        .WithRootUsername(username)
                        .WithRootPassword(password)
                        .WithComputerName("mysql")
                        .WithSize(VirtualMachineSizeTypes.StandardD2sV3)
                        .Create();

                    log.LogInformation($"{DateAndTime()} | Created | MySQL Virtual Machine");

                    #endregion

                    #region mongodb
                    INetworkSecurityGroup networkSecurityGroupmongo = _azureProd.NetworkSecurityGroups.Define($"{clusterName}-mongo-nsg")
                        .WithRegion(region)
                        .WithExistingResourceGroup(resourceGroup)
                        .DefineRule("ALLOW-SSH")
                            .AllowInbound()
                            .FromAnyAddress()
                            .FromAnyPort()
                            .ToAnyAddress()
                            .ToPort(22)
                            .WithProtocol(SecurityRuleProtocol.Tcp)
                            .WithPriority(100)
                            .WithDescription("Allow SSH")
                            .Attach()
                        .DefineRule("mongodb")
                            .AllowInbound()
                            .FromAnyAddress()
                            .FromAnyPort()
                            .ToAnyAddress()
                            .ToPort(27017)
                            .WithProtocol(SecurityRuleProtocol.Tcp)
                            .WithPriority(101)
                            .WithDescription("mongodb")
                            .Attach()
                        .Create();

                    log.LogInformation($"{DateAndTime()} | Created | MongoDB Network Security Group");

                    INetworkInterface networkInterfacemongo = _azureProd.NetworkInterfaces.Define($"{clusterName}-mongo-nic")
                        .WithRegion(region)
                        .WithExistingResourceGroup(resourceGroup)
                        .WithExistingPrimaryNetwork(virtualNetwork)
                        .WithSubnet(subnet)
                        .WithPrimaryPrivateIPAddressDynamic()
                        .WithExistingNetworkSecurityGroup(networkSecurityGroupmongo)
                        .Create();

                    log.LogInformation($"{DateAndTime()} | Created | MongoDB Network Security Group");

                    IVirtualMachine createVmmongo = _azureProd.VirtualMachines.Define($"{clusterName}-mongo")
                        .WithRegion(region)
                        .WithExistingResourceGroup(resourceGroup)
                        .WithExistingPrimaryNetworkInterface(networkInterfacemongo)
                        .WithStoredLinuxImage(vhdURL)
                        .WithRootUsername(username)
                        .WithRootPassword(password)
                        .WithComputerName("mongo")
                        .WithSize(VirtualMachineSizeTypes.StandardD2sV3)
                        .Create();

                    log.LogInformation($"{DateAndTime()} | Created | MongoDB Virtual Machine");

                    #endregion

                    log.LogInformation($"Done");
                }
                catch (Exception e)
                {
                    log.LogInformation($"{DateAndTime()} | Error | {e.Message}");

                    return new BadRequestObjectResult(false);
                }

                log.LogInformation($"{DateAndTime()} | Done");
                return new OkObjectResult(true);
            }
        }

        private static string DateAndTime() {
            return $"{ DateTime.Now.ToShortDateString()} { DateTime.Now.ToShortTimeString()}";
        }
    }
}
