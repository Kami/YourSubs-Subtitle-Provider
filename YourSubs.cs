using System;
using System.Drawing;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Web;
using System.Text.RegularExpressions;
using Sublight.Plugins.SubtitleProvider.Types;
using System.Diagnostics;

namespace Sublight.Plugins.SubtitleProvider.BuiltIn
{
    public class YourSubs : BaseSubtitleProvider, ISubtitleProvider
    {
        #region ISubtitleProvider Members

        public SubtitleBasicInfo[] Search(string title, SearchFilter[] filter, out string error)
        {
            try
            {
                error = null;

                string response = WebClient.DownloadString(string.Format("http://www.yoursubs.org/ajax-search-all.php?query={0}", HttpUtility.UrlEncode(title)));

                if (string.IsNullOrEmpty(response))
                    return null;

                int startIndexSuggestions = response.IndexOf("suggestions:[");
                int startIndexData = response.IndexOf("data:[");

                int endIndexSuggestions = response.IndexOf("]", startIndexSuggestions);
                int endIndexData = response.LastIndexOf("]");

                int lengthSuggestions = endIndexSuggestions - (startIndexSuggestions + 13);
                int lengthData = endIndexData - (startIndexData + 6);

                string suggestions = response.Substring(startIndexSuggestions + 13, lengthSuggestions);
                string ids = response.Substring(startIndexData + 6, lengthData);

                RegExps.Suggestion regExpSuggestion = new RegExps.Suggestion();
                RegExps.Id regExpId = new RegExps.Id();
                RegExps.Seasons regExpSeasons = new RegExps.Seasons();

                MatchCollection matchesSuggestions = regExpSuggestion.Matches(suggestions);
                MatchCollection matchesIds = regExpId.Matches(ids);

                int matchesCount = matchesIds.Count;

                // No results
                if (matchesCount == 0)
                    return null;

                string dataMovies = "";
                string dataSeries = "";
                string suggestion, id;

                for (int i = 0; i < matchesCount; i++)
                {
                    suggestion = matchesSuggestions[i].Groups["suggestion"].Value;
                    id = matchesIds[i].Groups["id"].Value;

                   response = WebClient.DownloadString(string.Format("http://www.yoursubs.org/title/{0}", id));

                    if (suggestion.IndexOf("movie") != -1)
                    {
                        dataMovies += response;
                    }
                    else
                    {
                        // Huston we have a problem - we can't fetch the list of all the subtitles for a TV series in one go
                        // so we need to fetch subtitles for each season individually (this can be slow).
                        MatchCollection seasons = regExpSeasons.Matches(response);

                        string seasonUrl;

                        foreach (Match season in seasons)
                        {
                            if (season.Groups["seasonUrl"] == null || !season.Groups["seasonUrl"].Success)
                                continue;

                            seasonUrl = season.Groups["seasonUrl"].Value.Trim();
                            response = WebClient.DownloadString(string.Format("http://www.yoursubs.org/title/{0}/{1}/AllLanguages", id, seasonUrl));

                            if (string.IsNullOrEmpty(response))
                                continue;

                            dataSeries += response;
                        }
                    }
                }

                var lstResult = new List<SubtitleBasicInfo>();

                RegExps.Movies regExpMovies = new RegExps.Movies();
                RegExps.Series regExpSeries = new RegExps.Series();

                MatchCollection matchesMovies = regExpMovies.Matches(dataMovies);
                MatchCollection matchesSeries = regExpSeries.Matches(dataSeries);

                // First we loop over the movies
                foreach (Match match in matchesMovies)
                {
                    if (match.Groups["id"] == null || !match.Groups["id"].Success || match.Groups["title"] == null || !match.Groups["title"].Success)
                        continue;

                    string subId = match.Groups["id"].Value;
                    string subTitle = match.Groups["title"].Value.Trim();
                    string subPublisher = match.Groups["publisher"].Value.Trim();
                    string subLanguage = match.Groups["language"].Value.Trim();
                    Int16 subYear = ParseYear(subTitle, filter);

                    var si = new SubtitleInfo();
                    si.Id = subId;
                    si.Title = subTitle;
                    si.Release = subTitle;
                    si.Publisher = subPublisher;
                    si.Language = LanguageAbbreviationToLanguage(subLanguage);

                    if (subYear != -1)
                    {
                        si.Year = subYear;
                    }

                    if (DoesMatchFilter(si, filter))
                    {
                        lstResult.Add(si);

                        if (MaxResultsReached(filter, lstResult.Count))
                        {
                            break;
                        }
                    }
                }

                // And then the series
                foreach (Match match in matchesSeries)
                {
                    if (match.Groups["id"] == null || !match.Groups["id"].Success || match.Groups["title"] == null || !match.Groups["title"].Success)
                        continue;

                    string subId = match.Groups["id"].Value;
                    string subTitle = match.Groups["title"].Value.Trim();
                    string subPublisher = match.Groups["publisher"].Value.Trim();
                    string subLanguage = match.Groups["language"].Value.Trim();
                    Int16 subSeason = Int16.Parse(match.Groups["season"].Value.Trim());
                    Int16 subEpisode = Int16.Parse(match.Groups["episode"].Value.Trim());

                    var sbi = new SubtitleInfo();
                    sbi.Id = subId;
                    sbi.Title = subTitle;
                    sbi.Release = subTitle;
                    sbi.Publisher = subPublisher;
                    sbi.Language = LanguageAbbreviationToLanguage(subLanguage);
                    sbi.Season = subSeason;
                    sbi.Episode = subEpisode;

                    if (DoesMatchFilter(sbi, filter))
                    {
                        lstResult.Add(sbi);

                        if (MaxResultsReached(filter, lstResult.Count))
                        {
                            break;
                        }
                    }
                }

                return lstResult.ToArray();
            }
            catch (Exception ex)
            {
                error = string.Format("Error searching subtitles: {0}", ex.Message);
                return null;
            }
        }

        public byte[] DownloadById(string id)
        {
            try
            {
                return WebClient.DownloadData(String.Format("http://www.yoursubs.org/download/{0}", id));
            }
            catch
            {
                return null;
            }
        }

        public string GetDownloadUrl(string id)
        {
            return string.Format("http://www.yoursubs.org/download/{0}", id);
        }

        public SubtitleProviderDownloadType DownloadType
        {
            get { return SubtitleProviderDownloadType.Direct; }
        }

        public SubtitleAction[] GetActions(string id)
        {
            return new[] { GetVisitHomePage("http://www.yoursubs.org") };
        }

        #endregion

        #region ISubtitleProviderInfo Members

        public Image Logo
        {
            get
            {
                return YourSubsProvider.Properties.Resources.YourSubs;
            }
        }

        public string ShortName
        {
            get
            {
                return "YourSubs.org";
            }
        }

        public string Info
        {
            get
            {
                return "YourSubs.org subtitle provider";
            }
        }

        public Version Version
        {
            get
            {
                return new Version(1, 0, 0);
            }
        }

        #endregion

        #region LanguageMapper

        protected override LanguageMapperUtility LanguageMapper
        {
            get
            {
                if (m_LanguageMapper == null)
                {
                    m_LanguageMapper = new LanguageMapperUtility();
                    m_LanguageMapper.Map(Language.English, "en");
                    m_LanguageMapper.Map(Language.Greek, "gr");
                    m_LanguageMapper.Map(Language.French, "fr");
                    m_LanguageMapper.Map(Language.Spanish, "es");
                    m_LanguageMapper.Map(Language.Polish, "pl");
                    m_LanguageMapper.Map(Language.Russian, "ru");
                    m_LanguageMapper.Map(Language.Italian, "it");
                    m_LanguageMapper.Map(Language.Hungarian, "hu");
                    m_LanguageMapper.Map(Language.Turkish, "tr");
                    m_LanguageMapper.Map(Language.German, "de");
                    m_LanguageMapper.Map(Language.Portuguese, "pt");
                    m_LanguageMapper.Map(Language.Hebrew, "il");
                    m_LanguageMapper.Map(Language.Croatian, "hr");
                    m_LanguageMapper.Map(Language.Dutch, "nl");
                    m_LanguageMapper.Map(Language.Romanian, "ro");
                    m_LanguageMapper.Map(Language.Czech, "cz");
                    m_LanguageMapper.Map(Language.Danish, "dk");
                    m_LanguageMapper.Map(Language.Swedish, "se");
                    m_LanguageMapper.Map(Language.Arabic, "sa");
                    m_LanguageMapper.Map(Language.Slovak, "sk");
                }

                return m_LanguageMapper;
            }
        }

        private LanguageMapperUtility m_LanguageMapper;

        #endregion

        #region WebClient
        protected WebClient WebClient
        {
            get
            {
                if (m_WebClient == null)
                {
                    m_WebClient = new WebClient();

                    var client = new WebClient();
                    client.Headers.Add("Connection", "close");
                    client.Headers.Add("User-Agent", UserAgent);
                }

                return m_WebClient;
            }
        }

        private WebClient m_WebClient;
        #endregion

        #region Methods

        private Language LanguageAbbreviationToLanguage(string abbreviation)
        {
            try
            {
                return LanguageMapper.GetLanguage(abbreviation);
            }
            catch
            {
                return Language.Unknown;
            }
        }

        private Int16 ParseYear(string title, SearchFilter[] filter)
        {
            bool hasYearFilter = false;
            string year;

            if (filter != null)
            {
                foreach (SearchFilter sf in filter)
                {
                    if (sf is Sublight.Plugins.SubtitleProvider.Types.SearchFilterYear)
                    {
                        hasYearFilter = true;
                        break;
                    }
                }
            }

            if (!hasYearFilter || string.IsNullOrEmpty(title))
            {
                return -1;
            }

            RegExps.Year regExpYear = new RegExps.Year();
            MatchCollection matches = regExpYear.Matches(title);

            foreach (Match match in matches)
            {
                year = match.Groups["year"].Value;

                if (string.IsNullOrEmpty(year))
                {
                    continue;
                }

                try
                {
                    return Int16.Parse(year);
                }
                catch
                {
                    continue;
                }
            }

            return -1;
        }

        private Int16 ParseEpisodeNumber(string title, SearchFilter[] filter)
        {
            bool hasEpisodeFilter = false;
            string episodeNumber;

            if (filter != null)
            {
                foreach (SearchFilter sf in filter)
                {
                    if (sf is Sublight.Plugins.SubtitleProvider.Types.SearchFilterEpisode)
                    {
                        hasEpisodeFilter = true;
                        break;
                    }
                }
            }

            if (!hasEpisodeFilter || string.IsNullOrEmpty(title))
            {
                return -1;
            }

            MatchCollection matches = Regex.Matches(title, @"E(?<episode>[0-9][0-9])|[0-9]+x(?<episode>[0-9][0-9])", RegexOptions.IgnoreCase);

            foreach (Match match in matches)
            {
                episodeNumber = match.Groups["episode"].Value;

                if (string.IsNullOrEmpty(episodeNumber))
                {
                    continue;
                }

                try
                {
                    return Int16.Parse(episodeNumber);
                }
                catch
                {
                    continue;
                }
            }

            return -1;
        }

        #endregion
    }
}
