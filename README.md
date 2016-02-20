# AzureLogFetch

A command line application that you can run which will download all your IIS logs from Azure and delete them from the 
cloud. You can create a .bat file and set it in task scheduler to run automatically. It will email you upon success or failure.

# Acknowledgements

This is based upon the original AzureLogFetch project from https://code.msdn.microsoft.com/Azure-Log-Fetcher-522ff173

# Prerequisites

* Visual Studio 2013.
* .NET Framework 4.5.

# Usage:

	Usage: AzureLogFetch <destination dir> <azure account name> <azure account key>
						 [<source directory>] [options]
	
	  <destination dir>     local directory to save files to
	  <azure account name>  Azure storage account name
	  <azure account key>   Azure storage account key
	  <source dir>          directory within wad-iis-logfiles container to download
	
	Options:
	  /delete               delete files from Azure storage after download
	
	Minimum and maximum age of files to download:
	  /minage <age>         mininum file age - exclude files newer than i.e. 1h / 1d / 1y
	  /maxage <age>         maximum file age - exclude files older than i.e. 1h / 1d / 1y
	
	Send an email report after downloading files:
	  /smtp <smtp server>
	  /email <recipient address>
        
# Examples

Example w/o email:

    AzureLogFetch g:\logs MyAzure EK247tO8q4aNLA+A==
    
Example w/ email:

    AzureLogFetch g:\logs MyAzure EK247tO8q4aNLA+A== smtp.mymail.com me@email.com
    
Example download files between 1 hour and 30 days old:

    AzureLogFetch g:\logs MyAzure EK247tO8q4aNLA+A== /minage 1h /maxage 30d
    
Example download files between 1 hour and 1 year old:

    AzureLogFetch g:\logs MyAzure EK247tO8q4aNLA+A== /minage 1h /maxage 1y
