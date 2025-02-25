using System;
using System.Net;
using System.Text.RegularExpressions;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using HtmlAgilityPack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;

using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection.Extensions;

public class Program
{
    static async Task Main(string[] args)
    {

        //create service collection instance 
        var servicecollection = new ServiceCollection();

        //build the configuration 
        var configuration = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", false)
        .Build();

        //add configuration to service 
        servicecollection.AddSingleton<IConfiguration>(configuration);

        //build service collection 
        var provider = servicecollection.BuildServiceProvider();

        //get required service 
        var config = provider.GetRequiredService<IConfiguration>();

        // getting paths from configuration (bin/debug/net7.0/appsettings.json)

        string? DomainPath = config["Paths:DomainsPath"];
        string? keyword = config["Paths:KeywordsPath"];
        string? Savedurls = config["Paths:SavedUrlPath"];
        string? Extendedurls = config["Paths:ExtendedUrlPath"];
        string? SavedEmails = config["Paths:EmailsPath"];


        var ap = new PageService();

        if (File.Exists(keyword))
        {

            string[] keywords = File.ReadAllLines(keyword);

            //format keywords incase there is space 
            var formattedkeywords = ap.KeywordsFormatter(keywords);

            int pagenumber = 0;
            char choice = 'n';

            Console.Write("1 .=> Search with Keyword  \n2 .=> Search with Url \n");

            int answer = Convert.ToInt32(Console.ReadLine());

            switch (answer)
            {
                case 1:

                    Console.WriteLine("Do you want to save Urls ?? Y/N");
                    choice = Convert.ToChar(Console.ReadLine());

                    foreach (var word in formattedkeywords)
                    {
                        string url = $"https://www.yellowpages.com.vn/srch/{word}.html";

                        //send request 
                        await ap.SearchUrlAsync(url, pagenumber, word, DomainPath, Savedurls, choice, SavedEmails);
                    }
                    break;

                case 2:

                    Console.WriteLine("Do you want to save Extended Urls ?? Y/N");
                    choice = Convert.ToChar(Console.ReadLine());

                    Console.WriteLine("Searching for saved urls");

                    if (File.Exists(Savedurls))
                    {
                        string[] savedurls = File.ReadAllLines(Savedurls);
                        foreach (var url in savedurls)
                        {
                            await ap.SearchUrlAsync(url, pagenumber, null, DomainPath, Extendedurls, choice, SavedEmails);
                        }

                    }
                    break;

                default:
                    Console.WriteLine("You did not provide recognized input");
                    break;

            }
        }
        else
        {
            Console.WriteLine("File path not found.Please check and try again....");
        }

    }
}

public class PageService
{
    private IServiceProvider _service;
    private IHttpClientFactory _httpclient;
    private HashSet<string> VnDomais = new HashSet<string>();

    private HashSet<string> SavedEmails = new HashSet<string>();
    private HashSet<string> SavedUrlToSearch = new HashSet<string>();

    private readonly string[] _DomainsNotToCollect;
    private bool IsSaved = false;

    public PageService()
    {
        _service = new ServiceCollection().AddHttpClient().BuildServiceProvider();

        _httpclient = _service.GetService<IHttpClientFactory>();
        _DomainsNotToCollect = new string[] {
            "YellowPage",
            "Facebook",
            "Twitter",
            "Threads",
            "w3.org",
            "instagram",
            "snapchat",
            "zalo",
            "line",
            "wechat",
            "viber",
            "google",
            "bitly"
        };
    }

    public HashSet<string> KeywordsFormatter(string[] words)
    {
        HashSet<string> keywords = new HashSet<string>();
        string fillgap = string.Empty;
        foreach (var word in words)
        {
            string lowercase = word.ToLower();
            if (!lowercase.Contains(' '))
            {
                keywords.Add(lowercase);
            }
            fillgap = lowercase.Replace(' ', '-');
            keywords.Add(fillgap);
        }
        return keywords;
    }

    public async Task SearchUrlAsync(string url, int startpage, string? word, string SaveDomainPath, string SaveUrlPath, char choice, string SavedEmailsPath)
    {
        string htmlcontent = string.Empty;
        string correcturl = string.Empty;
        string id = string.Empty;


        int maxpage = 0;

        // page will be 0 by default
        try
        {

            while (true)
            {
                if (!url.Contains("listing"))
                {
                    if (startpage > 0)
                    {
                        string fmturl = $"{url}&page={startpage}";
                        correcturl = fmturl;
                    }
                    else
                    {
                        correcturl = url;
                    }

                }
                else
                {
                    correcturl = url;
                }


                var policy = Policy.Handle<NullReferenceException>()
                .Or<TaskCanceledException>()
                .OrResult<HttpResponseMessage>(r => r.StatusCode == HttpStatusCode.RequestTimeout)
                .WaitAndRetryAsync(3, retries => TimeSpan.FromSeconds(Math.Pow(2, retries)), onRetryAsync: async (exception, retrycount, context) =>
            {
                if (context?.OperationKey != null)
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, context.OperationKey.ToString());
                }
            });
                var timeout = Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(300));

                var combinepolicies = Policy.WrapAsync(policy, timeout);
                var response = await combinepolicies.ExecuteAsync(async (context) =>
                {
                    var client = _httpclient.CreateClient();
                    using (var request = new HttpRequestMessage(HttpMethod.Get, correcturl))
                    {

                        request.Headers.Connection.Add("keep-alive");
                        request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7");
                        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Linux; Android 5.1; HTC One M9 Build/LMY47O) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/45.0.2454.94 Mobile Safari/537.36");
                        request.Headers.AcceptEncoding.ParseAdd("utf-8");
                        request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.5");

                        return await client.SendAsync(request);
                    }
                }, new Context(correcturl));

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine("Not success");
                    return;

                }
                htmlcontent = response.Content.ReadAsStringAsync().Result;

                if (url.Contains("listing"))
                {
                    bool saved = await GetEmailAsync(htmlcontent, SavedEmailsPath);
                    if (saved)
                    {
                        string[] UrlToArray = url.Split('/');
                        id = UrlToArray[4];
                        Console.WriteLine("Email Found for {0}", id);
                    }
                    break;
                }

                //if maxpage is 0 then get maxpage value from response body
                if (maxpage == 0)
                {
                    var doc = new HtmlDocument();
                    doc.LoadHtml(htmlcontent);
                    //get maxpage number from html 
                    var getmaxpage = doc.DocumentNode.SelectNodes("//div[@id='paging']/a").Where(n => n.Attributes["href"].Value.Contains("page=")).Select(node => node.InnerText.Trim()).ToArray();

                    int number = getmaxpage.Length - 1;
                    maxpage = Convert.ToInt32(getmaxpage[number - 1]);
                    maxpage--;

                }

                //get domains from response body 
                var gotdomains = await GetDomainsAsync(htmlcontent, SaveDomainPath);

                //get emails from response 
                bool savedemail = await GetEmailAsync(htmlcontent, SavedEmailsPath);

                if (word == null)
                {
                    string[] urlToArray = url.Split('/');
                    id = urlToArray[4];
                }

                string txt = $"page {startpage} keyword: {word} ";

                if (savedemail)
                {
                    Console.WriteLine("Email Found for {0}", string.IsNullOrEmpty(id) ? txt : id);
                }
                else
                {
                    Console.WriteLine("Email Not Found for {0}", string.IsNullOrEmpty(id) ? txt : id);
                }

                if (gotdomains)
                {
                    Console.WriteLine("Domains saved from {0} page with {1}", startpage == 0 ? "default" : startpage, string.IsNullOrEmpty(word) ? id : "Keyword: " + word);
                }
                else
                {
                    Console.WriteLine("No unique domain on page {0} for {1}", startpage == 0 ? "default" : startpage, string.IsNullOrEmpty(word) ? id : "Keyword: " + word);

                }

                if (choice == 'Y' || choice == 'y')
                {
                    //get otherurls to search from response body 
                    var goturlstosearch = await GetOtherUrlsToSearchAsync(htmlcontent, SaveUrlPath);

                    if (goturlstosearch) Console.WriteLine("Saved some link to search ");

                }

                if (startpage >= maxpage) break;

                startpage++;

            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            return;
        }
    }

    public async Task<bool> GetEmailAsync(string htmlcontent, string PathToSaveEmails)
    {
        IsSaved = false;

        // string domainpattern = @"https?://(?:www\.)?([a-zA-Z0-9-]+\.[a-zA-Z]{2,})(?:/|$)";
        string emailpattern = @"href\s*=\s*['""]mailto:([a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,})['""]";

        MatchCollection emailmatch = Regex.Matches(htmlcontent, emailpattern);

        try
        {
            foreach (Match item in emailmatch)
            {


                string email = item.Groups[1].Value.ToString();

                //Check if email is dirty 
                bool IsUrlDirty = _DomainsNotToCollect.Any(x => email.Contains(x, StringComparison.OrdinalIgnoreCase));


                if (!IsUrlDirty)
                {
                    if (SavedEmails != null)
                    {
                        if (!SavedEmails.Contains(email))
                        {
                            using (var sw = File.AppendText(PathToSaveEmails))
                            {
                                await sw.WriteLineAsync(email);
                            }

                            IsSaved = true;
                            SavedEmails.Add(email);

                        }

                    }

                }
            }

        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
        return IsSaved;

    }


    public async Task<bool> GetDomainsAsync(string htmlcontent, string PathToSaveDomains)
    {
        IsSaved = false;

        string pattern = @"https?://(?:www\.)?([a-zA-Z0-9-]+\.[a-zA-Z]{2,})(?:/|$)";

        MatchCollection domainmatch = Regex.Matches(htmlcontent, pattern);

        try
        {
            foreach (Match item in domainmatch)
            {

                string domain = item.Groups[1].Value.ToString();

                //Check if email is dirty 

                bool IsUrlDirty = _DomainsNotToCollect.Any(x => domain.Contains(x, StringComparison.OrdinalIgnoreCase));


                if (!IsUrlDirty)
                {
                    if (VnDomais != null)
                    {
                        if (!VnDomais.Contains(domain))
                        {
                            using (var sw = File.AppendText(PathToSaveDomains))
                            {
                                await sw.WriteLineAsync(domain);
                            }

                            IsSaved = true;
                            VnDomais.Add(domain);
                        }

                    }

                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
        return IsSaved;

    }
    public async Task<bool> GetOtherUrlsToSearchAsync(string htmlcontent, string PathToSaveUrls)
    {
        IsSaved = false;

        //Link regex to match
        string otherlinkpattern = @"<a\s+href=""(https?://)?(www\.)?([^""]+?)""[^>]*>(.*?)</a>";

        //regex pattern for clean url to search after getting lenghty linktext
        string urlpattern = @"href=[""'](https?://[^""'>]+)[""']";


        MatchCollection otherlinkmatch = Regex.Matches(htmlcontent, otherlinkpattern);

        try
        {
            foreach (var item in otherlinkmatch)
            {
                string? link = item.ToString();

                if (link != null)
                {

                    Match urlmatch = Regex.Match(link, urlpattern);

                    string url = urlmatch.Groups[1].Value.ToString();
                    if (url.Contains("categories") || url.Contains("listing"))
                    {
                        //First check if url is already saved 
                        if (!SavedUrlToSearch.Contains(url))
                        {
                            using (var sw = File.AppendText(PathToSaveUrls))
                            {
                                await sw.WriteLineAsync(url);
                            }

                            IsSaved = true;

                            SavedUrlToSearch.Add(url);

                        }

                    }
                }
            }

        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);

        }
        return IsSaved;

    }
}