using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ZTMZ.PacenoteTool
{
    public class ProfileManager
    {
        public string CurrentProfile { set; get; }
        public string CurrentItineraryPath { set; get; }
        public ProfileManager()
        {
            Directory.CreateDirectory(string.Format("profiles"));
            this.CreateNewProfile("default");
        }

        public List<string> GetAllProfiles()
        {
            return Directory.GetDirectories("profiles/").ToList();
        }

        public void CreateNewProfile(string profileName)
        {
            Directory.CreateDirectory(string.Format("profiles/{0}", profileName));
            this.CurrentProfile = profileName;
        }

        public string StartRecording(string itinerary)
        {
            string filesPath = string.Format("profiles/{0}/{1}", this.CurrentProfile, itinerary);
            Directory.CreateDirectory(filesPath);
            this.CurrentItineraryPath = filesPath;
            return filesPath;
        }

        public void EndRecording(string itinerary, string codriver)
        {
            // save the codriver name to config
        }

        public void StartReplaying(string itinerary)
        {
            // load codriver name
        }
    }
}