using System;
using System.Collections.Generic;
using System.Text;

namespace ProvisionOpenEdXPlatform
{
    class ProvisioningModelWindows
    {
        public string ResourceGroupName { get; set; }
        public string ClustrerName { get; set; }
        public string TenantId { get; set; }
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string SubscriptionId { get; set; }
        public string MainVhdURL { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
    }
}
