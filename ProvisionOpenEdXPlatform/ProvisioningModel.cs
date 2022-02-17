using System;
using System.Collections.Generic;
using System.Text;

namespace ProvisionOpenEdXPlatform
{
    public class ProvisioningModel
    {
        public string ResourceGroupName { get; set; }
        public string ClustrerName { get; set; }
        public string TenantId { get; set; }
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string SubscriptionId { get; set; }
        public string MainVhdURL { get; set; }
        public string MysqlVhdURL { get; set; }
        public string MongoVhdURL { get; set; }
        public string InstanceCount { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string SmtpServer { get; set; }
        public int SmtpPort { get; set; }
        public string SmtpEmail { get; set; }
        public string SmtpPassword { get; set; }
        public string InsightVhdURL { get; set; }
    }
}
