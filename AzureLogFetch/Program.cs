using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Net.Mail;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;

namespace AzureLogFetch
{
    class Program
    {
        static string smtp = null;
        static string email = null;

        static void Main(string[] args)
        {
            if (args.Count() < 3)
            {
                Console.WriteLine("Usage: AzureLogFetch [path to save files] [Azure instance name] [Azure key] [smtp port] [email]");
                Console.WriteLine(@"Note: smtpport and email are optional");
                Console.WriteLine(@"Example w/o email:");
                Console.WriteLine(@"Example: AzureLogFetch g:\logs MyAzure EK247tO8q4aNLA+A==");
                Console.WriteLine(@"Example w/ email: AzureLogFetch g:\logs MyAzure EK247tO8q4aNLA+A== smtp.mymail.com me@email.com");
                return;
            }
            string directory = args[0];
            string instance = args[1];
            string key = args[2];

            if (args.Count() > 3)
            {
                smtp = args[3];
                email = args[4];
            }

            var account = new CloudStorageAccount(
        new StorageCredentials(
            instance,
            key
            ),
        false
        );

            var client = account.CreateCloudBlobClient();
            var container = client.GetContainerReference("wad-iis-logfiles");

            //note that I pass the BlobRequestOptions of UseFlatBlobListing which returns all files regardless 
            //of nesting so that I don't have to walk the directory structure
            foreach (var blob in container.ListBlobs(useFlatBlobListing: true, blobListingDetails: BlobListingDetails.Metadata))
            {
                CloudBlob b = blob as CloudBlob;
                try
                {
                    b.FetchAttributes();
                    DateTime lastModified = b.Properties.LastModified.GetValueOrDefault(DateTime.UtcNow).DateTime;
                    TimeSpan span = DateTime.UtcNow.Subtract(lastModified);
                    int compare = TimeSpan.Compare(span, TimeSpan.FromHours(1));
                    //we don't want to download and delete the latest log file, because it is incomplete and still being
                    //written to, thus this compare logic
                    if (compare == 1)
                    {
                        b.DownloadToFile(directory + b.Uri.PathAndQuery, FileMode.Create);
                        b.Delete();
                    }
                }
                catch (Exception e)
                {
                    SendMail(instance + " download of logs failed!!!!", e.Message);
                    return;
                }

            }
            SendMail(instance + " download of logs complete at " + DateTime.Now.ToLongDateString() + " " + DateTime.Now.ToShortTimeString(), "");


        }
        static void SendMail(string subject, string body)
        {
            if (smtp == null || email == null)
            {
                return;
            }
            MailMessage message = new MailMessage();
            SmtpClient smtpClient = new SmtpClient(smtp, 25);
            message.From = new MailAddress(email);
            message.To.Add(new MailAddress(email));
            message.Subject = subject;
            message.Body = body;
            smtpClient.UseDefaultCredentials = true;
            smtpClient.Send(message);

        }
    }
}
