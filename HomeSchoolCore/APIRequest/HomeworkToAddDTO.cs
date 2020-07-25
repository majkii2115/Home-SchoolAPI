using System;
using System.Collections.Generic;

namespace HomeSchoolCore.APIRequest
{
    public class HomeworkToAddDTO
    {
        public string subjectID { get; set; }
        public string classID { get; set; }
        public string name { get; set; }
        public string description { get; set; }
        public DateTime time { get; set; }
        public List<string> filesID { get; set; }
        public List<string> linkHrefs { get; set; }
    }
}