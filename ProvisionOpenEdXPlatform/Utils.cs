using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Mail;
using System.Text;

namespace ProvisionOpenEdXPlatform
{
    public class Utils
    {
        public static void Email(SmtpClient smtp, string htmlString, ILogger log, MailMessage message, string attachmentPath = "")
        {
            try
            {
                if (!attachmentPath.Equals(string.Empty))
                {
                    message.Attachments.Add(new Attachment(attachmentPath));
                }
                message.IsBodyHtml = true;
                message.Body = htmlString;
                smtp.DeliveryMethod = SmtpDeliveryMethod.Network;
                smtp.Send(message);
            }
            catch (Exception e)
            {
                log.LogInformation(e.Message);
            }
        }
       
    }
}
