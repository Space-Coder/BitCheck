using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace BitCheck
{
    public class Balance
    {
        public int final_balance { get; set; }
        public int n_tx { get; set; }
        public int total_received { get; set; }
    }
}




