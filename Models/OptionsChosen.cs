using System;

namespace CompetitionApp.Models
{
    public class OptionsChosen
    {
        public int id { get; set; }
        public int user_id { get; set; }
        public string firstName { get; set; }
        public string lastName { get; set; }
        public int option_1_id { get; set; }
        public string option_1_name { get; set; }
        public int option_2_id { get; set; }
        public string option_2_name { get; set; }
        public int option_3_id { get; set; }
        public string option_3_name { get; set; }
        public int option_4_id { get; set; }
        public string option_4_name { get; set; }
        public int option_5_id { get; set; }
        public string option_5_name { get; set; }
        public int option_6_id { get; set; }
        public string option_6_name { get; set; }
        public int option_7_id { get; set; }
        public string option_7_name { get; set; }
        public int option_8_id { get; set; }
        public string option_8_name { get; set; }
        public int option_9_id { get; set; }
        public string option_9_name { get; set; }
        public int option_10_id { get; set; }
        public string option_10_name { get; set; }
        public int? total_point { get; set; }
        public int? total_point2 { get; set; }
        public DateTime date_added { get; set; }
        public DateTime date_last_updated { get; set; }

        public int contestRegistered { get; set; }
    }
}
