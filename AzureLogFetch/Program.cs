using System;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;

namespace AzureLogFetch
{
    class Program
    {
        static string smtp;
        static string email;
        static TimeSpan? minAge;
        static TimeSpan? maxAge;
        static bool deleteBlobs;

        private const int MaxConcurrentDownloads = 4;
        private static readonly Semaphore semaphore = new Semaphore(MaxConcurrentDownloads, MaxConcurrentDownloads);

        public static void Main(string[] args)
        {
            if (args.Length < 3 ||
                args.Any(arg => arg.ToLower().StartsWith("/?") ||
                                arg.ToLower().StartsWith("/h") ||
                                arg.ToLower().StartsWith("-?") ||
                                arg.ToLower().StartsWith("-h")))
            {
                Console.WriteLine("Usage: AzureLogFetch <destination dir> <azure account name> <azure account key>");
                Console.WriteLine("                     [<source directory>] [options]");
                Console.WriteLine();
                Console.WriteLine("  <destination dir>  local directory to save files to");
                Console.WriteLine("  <source dir>       directory within wad-iis-logfiles container to download");
                Console.WriteLine();
                Console.WriteLine();
                Console.WriteLine("Options:");
                Console.WriteLine("  /delete        delete files from Azure storage after download");
                Console.WriteLine();
                Console.WriteLine("Minimum and maximum age of files to download:");
                Console.WriteLine("  /minage <age>  mininum file age - exclude files newer than i.e. 1h / 1d / 1y");
                Console.WriteLine("  /maxage <age>  maximum file age - exclude files older than i.e. 1h / 1d / 1y");
                Console.WriteLine();
                Console.WriteLine("Send an email report after downloading files:");
                Console.WriteLine("  /smtp <smtp server>");
                Console.WriteLine("  /email <recipient address>");
                Console.WriteLine();
                Console.WriteLine("Example w/o email:");
                Console.WriteLine("    AzureLogFetch g:\\logs MyAzure EK247tO8q4aNLA+A==");
                Console.WriteLine("Example w/ email:");
                Console.WriteLine("    AzureLogFetch g:\\logs MyAzure EK247tO8q4aNLA+A== smtp.mymail.com me@email.com");
                Console.WriteLine("Example download files between 1 hour and 1 year old:");
                Console.WriteLine("    AzureLogFetch g:\\logs MyAzure EK247tO8q4aNLA+A== /minage 1h /maxage 1y");
                return;
            }

            string destDir = args[0];
            string accountName = args[1];
            string accountKey = args[2];
            string sourceDir = null;

            try
            {
                for (int i = 3; i < args.Length; i++)
                {
                    string arg = args[i];

                    if (arg[0] == '/' || arg[0] == '-')
                    {
                        switch (arg.Substring(1).ToLower())
                        {
                            case "smtp":
                                smtp = args[++i];
                                break;
                            case "email":
                                email = args[++i];
                                break;
                            case "delete":
                                deleteBlobs = true;
                                break;
                            case "minage":
                                minAge = ParseAge(args[++i]);
                                break;
                            case "maxage":
                                maxAge = ParseAge(args[++i]);
                                break;
                            default:
                                throw new FormatException("Invalid argument: " + arg);
                        }
                    }
                    else
                    {
                        if (i == 3)
                        {
                            sourceDir = arg;
                        }
                        else throw new FormatException("Invalid argument: " + arg);
                    }
                }
            }
            catch (FormatException ex)
            {
                Console.WriteLine(ex.Message);
                return;
            }
            catch (IndexOutOfRangeException)
            {
                Console.WriteLine("Invalid arguments");
                return;
            }

            try
            {
                Console.WriteLine("Connecting to storage...");
                CloudStorageAccount storageAccount = new CloudStorageAccount(new StorageCredentials(accountName, accountKey), true);
                CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
                CloudBlobContainer blobContainer = blobClient.GetContainerReference("wad-iis-logfiles");

                Console.WriteLine("Listing and downloading items...");

                int numSkipped = 0;
                int numDownloaded = 0;

                foreach (var b in blobContainer.ListBlobs(sourceDir, true, BlobListingDetails.Metadata))
                {
                    CloudBlob blob = (CloudBlob)b;

                    // blob name will be something like: WAD/<deploymentid>/<rolename>/<instancename>/W3SVCnnnnn/u_exNNNNNNNN.log
                    // we will convert the separators to underscores so it's WAD_<deploymentid>_<rolename>_<instancename>_W3SVCnnnnn_u_exNNNNNNNN.log
                    string localFileName = Path.Combine(destDir, blob.Name.Replace('/', '_'));

                    var localFileInfo = new FileInfo(localFileName);
                    var lastModified = blob.Properties.LastModified.GetValueOrDefault(DateTime.UtcNow).DateTime;

                    if (blob.Properties.Length > 0 &&
                        (!localFileInfo.Exists || blob.Properties.Length != localFileInfo.Length) &&
                        IsMinMaxAge(lastModified))
                    {
                        // wait for free download slot
                        semaphore.WaitOne();
                        Task.Run(() => DownloadFile(storageAccount, blob.Name, localFileName, lastModified));
                        numDownloaded++;
                    }
                    else
                    {
                        //Console.WriteLine("Skip: " + newName);
                        numSkipped++;
                    }
                }

                // wait for downloads to complete by exhausting the semaphore
                for (int i = 0; i < MaxConcurrentDownloads; i++)
                    semaphore.WaitOne();

                Console.WriteLine("{0} files skipped", numSkipped);
                Console.WriteLine("{0} files downloaded", numDownloaded);

                SendMail(accountName + " download of logs complete at " + DateTime.Now.ToLongDateString() + " " + DateTime.Now.ToShortTimeString(),
                    string.Format("{0} files skipped\n{1} files downloaded", numSkipped, numDownloaded));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                SendMail(accountName + " download of logs failed!", ex.Message);
            }
        }

        private static void DownloadFile(CloudStorageAccount storageAccount, string blobName, string localFileName, DateTime lastModified)
        {
            try
            {
                Console.WriteLine("Downloading... " + Path.GetFileName(localFileName));

                var blobClient = storageAccount.CreateCloudBlobClient();
                var blobContainer = blobClient.GetContainerReference("wad-iis-logfiles");
                var blob = blobContainer.GetBlobReference(blobName);

                blob.DownloadToFile(localFileName, FileMode.Create);

                File.SetCreationTimeUtc(localFileName, lastModified);
                File.SetLastWriteTimeUtc(localFileName, lastModified);

                if (deleteBlobs)
                    blob.Delete();
            }
            finally
            {
                semaphore.Release();
            }
        }

        private static TimeSpan ParseAge(string s)
        {
            Regex pattern = new Regex(@"(\d+)([hdy])", RegexOptions.IgnoreCase);
            Match match = pattern.Match(s);
            if (match == null) throw new FormatException();

            int value = int.Parse(match.Groups[1].Value);
            switch (match.Groups[2].Value.ToLower())
            {
                case "h": return TimeSpan.FromHours(value);
                case "d": return TimeSpan.FromDays(value);
                case "y": return TimeSpan.FromDays(value * 365);
                default: throw new FormatException();
            }
        }

        private static bool IsMinMaxAge(DateTime lastModified)
        {
            TimeSpan age = DateTime.UtcNow - lastModified;

            if (minAge.HasValue && age < minAge.Value)
                return false;

            if (maxAge.HasValue && age > maxAge.Value)
                return false;

            return true;
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
