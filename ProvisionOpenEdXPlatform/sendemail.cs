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
using System.Net;

namespace ProvisionOpenEdXPlatform
{
    public static class sendemail
    {
        [FunctionName("sendemail")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            string name = req.Query["name"];

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            name = name ?? data?.name;

            SmtpClient smtpClient = new SmtpClient()
            {
                Host = "smtp.office365.com",
                Port = 587,
                EnableSsl = true,
                UseDefaultCredentials = true,
                Credentials = new NetworkCredential("j-patrick@cloudswyft.com", "Jaypee23")
            };

            MailMessage mailMessage = new MailMessage();

            mailMessage.From = new MailAddress("j-patrick@cloudswyft.com");
            mailMessage.To.Add(new MailAddress("j-patrick@cloudswyft.com"));
            mailMessage.Subject = "Branch Academy Installation";
            Utils.Email(smtpClient, "Your Learning Platform is Ready to use." +
                        "<br/>"
                        + $"<a href=\"{"LMSLINK"}\">LMS</a>" +
                        "<br/>" +
                        $"<a href=\"{"CMSLINK"}\">CMS</a>"
                        , log, mailMessage);
            string responseMessage = string.IsNullOrEmpty(name)
                ? "This HTTP triggered function executed successfully. Pass a name in the query string or in the request body for a personalized response."
                : $"Hello, {name}. This HTTP triggered function executed successfully.";

            return new OkObjectResult(responseMessage);
        }
    }
}
