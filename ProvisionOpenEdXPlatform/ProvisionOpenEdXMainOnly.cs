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
using System.Net;


namespace ProvisionOpenEdXPlatform
{
    public static class ProvisionOpenEdXMainOnly
    {
        [FunctionName("ProvisionOpenEdXMainOnly")]

        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation($"{Utils.DateAndTime()} | C# HTTP trigger function processed a request.");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

            ProvisioningModel provisioningModel = JsonConvert.DeserializeObject<ProvisioningModel>(requestBody);


            if (string.IsNullOrEmpty(provisioningModel.ClientId) ||
                string.IsNullOrEmpty(provisioningModel.ClientSecret) ||
                string.IsNullOrEmpty(provisioningModel.TenantId) ||
                string.IsNullOrEmpty(provisioningModel.SubscriptionId) ||
                string.IsNullOrEmpty(provisioningModel.ClustrerName) ||
                string.IsNullOrEmpty(provisioningModel.ResourceGroupName) ||
                string.IsNullOrEmpty(provisioningModel.MainVhdURL) ||
                string.IsNullOrEmpty(provisioningModel.MysqlVhdURL) ||
                string.IsNullOrEmpty(provisioningModel.MongoVhdURL) ||
                string.IsNullOrEmpty(provisioningModel.SmtpServer) ||
                string.IsNullOrEmpty(provisioningModel.SmtpPort.ToString()) ||
                string.IsNullOrEmpty(provisioningModel.SmtpEmail) ||
                string.IsNullOrEmpty(provisioningModel.SmtpPassword))
            {
                log.LogInformation($"{Utils.DateAndTime()} | Error |  Missing parameter | \n{requestBody}");
                return new BadRequestObjectResult(false);
            }
            else
            {
                try
                {

                    string resourceGroupName = provisioningModel.ResourceGroupName;
                    string clusterName = provisioningModel.ClustrerName;
                    string MainVhdURL = provisioningModel.MainVhdURL;
                    string MysqlVhdURL = provisioningModel.MysqlVhdURL;
                    string MongoVhdURL = provisioningModel.MongoVhdURL;
                    string subnet = "default";
                    string username = provisioningModel.Username;
                    string password = provisioningModel.Password;

                    string contactPerson = provisioningModel.SmtpEmail;

                    log.LogInformation("deploying Main instance");
                    //Utils.Email(smtpClient, "Main Instance Deployed Successfully", log, mailMessage);

                    ServicePrincipalLoginInformation principalLogIn = new ServicePrincipalLoginInformation();
                    principalLogIn.ClientId = provisioningModel.ClientId;
                    principalLogIn.ClientSecret = provisioningModel.ClientSecret;

                    AzureEnvironment environment = AzureEnvironment.AzureGlobalCloud;
                    AzureCredentials credentials = new AzureCredentials(principalLogIn, provisioningModel.TenantId, environment);

                    IAzure _azureProd = Azure.Configure()
                          .WithLogLevel(HttpLoggingDelegatingHandler.Level.Basic)
                          .Authenticate(credentials)
                          .WithSubscription(provisioningModel.SubscriptionId);


                    IResourceGroup resourceGroup = _azureProd.ResourceGroups.GetByName(resourceGroupName);
                    Region region = resourceGroup.Region;

                    #region comment

                    #region Get Virtual Network
                    INetwork virtualNetwork = _azureProd.Networks.GetByResourceGroup($"{resourceGroup}", $"{clusterName}-vnet");

                    #endregion

                    log.LogInformation($"{Utils.DateAndTime()} | Detected | VNET");

                    #region Create VM IP
                    IPublicIPAddress publicIpAddress = _azureProd.PublicIPAddresses.Define($"{clusterName}-vm-ip")
                       .WithRegion(region)
                       .WithExistingResourceGroup(resourceGroupName)
                       .WithDynamicIP()
                       .WithLeafDomainLabel(clusterName)
                       .WithTag("_contact_person", contactPerson)
                       .Create();
                    #endregion

                    log.LogInformation($"{Utils.DateAndTime()} | Created | VM IP Address");

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
                        .DefineRule("CMSSSLPort")
                            .AllowInbound()
                            .FromAnyAddress()
                            .FromAnyPort()
                            .ToAnyAddress()
                            .ToPort(48010)
                            .WithProtocol(SecurityRuleProtocol.Tcp)
                            .WithPriority(112)
                            .WithDescription("CMSSSLPort")
                            .Attach()
                        .DefineRule("LMSSSLPort")
                            .AllowInbound()
                            .FromAnyAddress()
                            .FromAnyPort()
                            .ToAnyAddress()
                            .ToPort(443)
                            .WithProtocol(SecurityRuleProtocol.Tcp)
                            .WithPriority(122)
                            .WithDescription("LMSSSLPort")
                            .Attach()
                        .DefineRule("Certs")
                            .AllowInbound()
                            .FromAnyAddress()
                            .FromAnyPort()
                            .ToAnyAddress()
                            .ToPort(18090)
                            .WithProtocol(SecurityRuleProtocol.Tcp)
                            .WithPriority(132)
                            .WithDescription("Certs")
                            .Attach()
                        .DefineRule("Discovery")
                            .AllowInbound()
                            .FromAnyAddress()
                            .FromAnyPort()
                            .ToAnyAddress()
                            .ToPort(18381)
                            .WithProtocol(SecurityRuleProtocol.Tcp)
                            .WithPriority(142)
                            .WithDescription("Discovery")
                            .Attach()
                        .DefineRule("Ecommerce")
                            .AllowInbound()
                            .FromAnyAddress()
                            .FromAnyPort()
                            .ToAnyAddress()
                            .ToPort(18130)
                            .WithProtocol(SecurityRuleProtocol.Tcp)
                            .WithPriority(152)
                            .WithDescription("Ecommerce")
                            .Attach()
                        .DefineRule("edx-release")
                            .AllowInbound()
                            .FromAnyAddress()
                            .FromAnyPort()
                            .ToAnyAddress()
                            .ToPort(8099)
                            .WithProtocol(SecurityRuleProtocol.Tcp)
                            .WithPriority(162)
                            .WithDescription("edx-release")
                            .Attach()
                        .DefineRule("Forum")
                            .AllowInbound()
                            .FromAnyAddress()
                            .FromAnyPort()
                            .ToAnyAddress()
                            .ToPort(18080)
                            .WithProtocol(SecurityRuleProtocol.Tcp)
                            .WithPriority(172)
                            .WithDescription("Forum")
                            .Attach()
                        .WithTag("_contact_person", contactPerson)
                        .DefineRule("Xqueue")
                            .AllowInbound()
                            .FromAnyAddress()
                            .FromAnyPort()
                            .ToAnyAddress()
                            .ToPort(18040)
                            .WithProtocol(SecurityRuleProtocol.Tcp)
                            .WithPriority(182)
                            .WithDescription("Xqueue")
                            .Attach()
                        .WithTag("_contact_person", contactPerson)
                        .Create();
                    #endregion

                    log.LogInformation($"{Utils.DateAndTime()} | Created | Network Security Group");

                    #region nic
                    INetworkInterface networkInterface = _azureProd.NetworkInterfaces.Define($"{clusterName}-nic")
                        .WithRegion(region)
                        .WithExistingResourceGroup(resourceGroupName)
                        .WithExistingPrimaryNetwork(virtualNetwork)
                        .WithSubnet(subnet)
                        .WithPrimaryPrivateIPAddressDynamic()
                        .WithExistingPrimaryPublicIPAddress(publicIpAddress)
                        .WithExistingNetworkSecurityGroup(networkSecurityGroup)
                        .WithTag("_contact_person", contactPerson)
                        .Create();
                    #endregion

                    log.LogInformation($"{Utils.DateAndTime()} | Created | Network Interface");

                    IStorageAccount storageAccount = _azureProd.StorageAccounts.GetByResourceGroup(resourceGroupName, $"{"qabranchacademy"}vhdsa");

                    #region vm
                    IVirtualMachine createVm = _azureProd.VirtualMachines.Define($"{clusterName}-jb")
                        .WithRegion(region)
                        .WithExistingResourceGroup(resourceGroupName)
                        .WithExistingPrimaryNetworkInterface(networkInterface)
                        .WithStoredLinuxImage(MainVhdURL)
                        .WithRootUsername(username)
                        .WithRootPassword(password)
                        .WithComputerName(username)
                        .WithBootDiagnostics(storageAccount)
                        .WithSize(VirtualMachineSizeTypes.StandardD2sV3)
                        .WithTag("_contact_person", contactPerson)
                        .Create();
                    #endregion

                    log.LogInformation($"{Utils.DateAndTime()} | Created | Main Virtual Machine");

                    #region LMS IP
                    IPublicIPAddress publicIPAddressLMS = _azureProd.PublicIPAddresses.Define($"{clusterName}-lms-ip")
                        .WithRegion(region)
                        .WithExistingResourceGroup(resourceGroupName)
                        .WithDynamicIP()
                        .WithLeafDomainLabel($"{clusterName}-lms-ip")
                        .WithTag("_contact_person", contactPerson)
                        .Create();
                    #endregion

                    log.LogInformation($"{Utils.DateAndTime()} | Created | LMS Public IP Address");

                    #region CMS IP
                    IPublicIPAddress publicIPAddressCMS = _azureProd.PublicIPAddresses.Define($"{clusterName}-cms-ip")
                        .WithRegion(region)
                        .WithExistingResourceGroup(resourceGroupName)
                        .WithDynamicIP()
                        .WithLeafDomainLabel($"{clusterName}-cms-ip")
                        .WithTag("_contact_person", contactPerson)
                        .Create();
                    #endregion

                    log.LogInformation($"{Utils.DateAndTime()} | Created | CMS Public IP Address");

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
                        .WithTag("_contact_person", contactPerson)
                        .Create();
                    #endregion

                    log.LogInformation($"{Utils.DateAndTime()} | Created | Load Balancer");

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
                            .FromRegion(region)
                            .WithRoutingPriority(1)
                            .Attach()
                            .WithTag("_contact_person", contactPerson);

                    ITrafficManagerProfile trafficManagerProfileLMS = tmCreatableLMS.Create();

                    log.LogInformation($"{Utils.DateAndTime()} | Created | LMS Traffic Manager");

                    IWithEndpoint tmDefinitionCMS = _azureProd.TrafficManagerProfiles
                            .Define($"{clusterName}-cms-tm")
                            .WithExistingResourceGroup(resourceGroupName)
                            .WithLeafDomainLabel($"{clusterName}-cms-tm")
                            .WithPriorityBasedRouting();
                    ICreatable<ITrafficManagerProfile> tmCreatableCMS = null;

                    tmCreatableCMS = tmDefinitionCMS
                        .DefineExternalTargetEndpoint($"{clusterName}-cms-tm")
                            .ToFqdn(publicIPAddressCMS.Fqdn)
                            .FromRegion(region)
                            .WithRoutingPriority(1)
                            .Attach()
                            .WithTag("_contact_person", contactPerson);

                    ITrafficManagerProfile trafficManagerProfileCMS = tmCreatableCMS.Create();

                    log.LogInformation($"{Utils.DateAndTime()} | Created | CMS Traffic Manager");

                    #endregion

                    #endregion

                    #region MYSQL MONGO
                    /*
                    if (!isSingleInstance) {

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
                            .WithTag("_contact_person", contactPerson)
                            .Create();

                        log.LogInformation($"{Utils.DateAndTime()} | Created | MySQL Network Security Group");

                        INetworkInterface networkInterfacemysql = _azureProd.NetworkInterfaces.Define($"{clusterName}-mysql-nic")
                            .WithRegion(region)
                            .WithExistingResourceGroup(resourceGroup)
                            .WithExistingPrimaryNetwork(virtualNetwork)
                            .WithSubnet(subnet)
                            .WithPrimaryPrivateIPAddressDynamic()
                            .WithExistingNetworkSecurityGroup(networkSecurityGroupmysql)
                            .WithTag("_contact_person", contactPerson)
                            .Create();

                        log.LogInformation($"{Utils.DateAndTime()} | Created | MySQL Network Interface");

                        IVirtualMachine createVmmysql = _azureProd.VirtualMachines.Define($"{clusterName}-mysql")
                            .WithRegion(region)
                            .WithExistingResourceGroup(resourceGroup)
                            .WithExistingPrimaryNetworkInterface(networkInterfacemysql)
                            .WithStoredLinuxImage(MysqlVhdURL)
                            .WithRootUsername(username)
                            .WithRootPassword(password)
                            .WithComputerName("mysql")
                            .WithBootDiagnostics(storageAccount)
                            .WithSize(VirtualMachineSizeTypes.StandardD2V2)
                            .WithTag("_contact_person", contactPerson)
                            .Create();

                        log.LogInformation($"{Utils.DateAndTime()} | Created | MySQL Virtual Machine");

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
                            .WithTag("_contact_person", contactPerson)
                            .Create();

                        log.LogInformation($"{Utils.DateAndTime()} | Created | MongoDB Network Security Group");

                        INetworkInterface networkInterfacemongo = _azureProd.NetworkInterfaces.Define($"{clusterName}-mongo-nic")
                            .WithRegion(region)
                            .WithExistingResourceGroup(resourceGroup)
                            .WithExistingPrimaryNetwork(virtualNetwork)
                            .WithSubnet(subnet)
                            .WithPrimaryPrivateIPAddressDynamic()
                            .WithExistingNetworkSecurityGroup(networkSecurityGroupmongo)
                            .WithTag("_contact_person", contactPerson)
                            .Create();

                        log.LogInformation($"{Utils.DateAndTime()} | Created | MongoDB Network Interface");

                        IVirtualMachine createVmmongo = _azureProd.VirtualMachines.Define($"{clusterName}-mongo")
                            .WithRegion(region)
                            .WithExistingResourceGroup(resourceGroup)
                            .WithExistingPrimaryNetworkInterface(networkInterfacemongo)
                            .WithStoredLinuxImage(MongoVhdURL)
                            .WithRootUsername(username)
                            .WithRootPassword(password)
                            .WithComputerName("mongo")
                            .WithBootDiagnostics(storageAccount)
                            .WithSize(VirtualMachineSizeTypes.StandardD2V2)
                            .WithTag("_contact_person", contactPerson)
                            .Create();

                        log.LogInformation($"{Utils.DateAndTime()} | Created | MongoDB Virtual Machine");

                        #endregion

                        log.LogInformation("deploying 3 instance");

                        Utils.Email(smtpClient, "MySQL Instance Deployed Successfully", log, mailMessage);
                    }


                    string cmsUrl = trafficManagerProfileCMS.DnsLabel;
                    string lmsUrl = trafficManagerProfileLMS.DnsLabel;

                    Utils.Email(smtpClient, "Your Learning Platform is Ready to use." +
                        "<br/>"
                        + $"<a href=\"{lmsUrl}\">LMS</a>" +
                        "<br/>" +
                        $"<a href=\"{cmsUrl}\">CMS</a>"
                        , log, mailMessage);
                    */
                    #endregion

                    log.LogInformation($"Done");
                }
                catch (Exception e)
                {
                    log.LogInformation($"{Utils.DateAndTime()} | Error | {e.Message}");

                    return new BadRequestObjectResult(false);
                }

                log.LogInformation($"{Utils.DateAndTime()} | Done");
                return new OkObjectResult(true);
            }
        }
    }
}
