using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.Models
{
    public class Drawing
    {
        [JsonProperty("x")]
        public double X { get; set; }

        [JsonProperty("y")]
        public double Y { get; set; }

        [JsonProperty("type")]
        public long Type { get; set; }

        [JsonProperty("UUID")]
        public string Uuid { get; set; }

        [JsonProperty("selectedShape", NullValueHandling = NullValueHandling.Ignore)]
        public string SelectedShape { get; set; }

        [JsonProperty("selectedShapeOptions", NullValueHandling = NullValueHandling.Ignore)]
        public SelectedShapeOptions SelectedShapeOptions { get; set; }
    }

    public class SelectedShapeOptions
    {
        [JsonProperty("shouldFillShape")]
        public bool ShouldFillShape { get; set; }

        [JsonProperty("fillStyle")]
        public string FillStyle { get; set; }

        [JsonProperty("strokeStyle")]
        public string StrokeStyle { get; set; }

        [JsonProperty("lineWidth")]
        public long LineWidth { get; set; }

        [JsonProperty("lineJoin")]
        public string LineJoin { get; set; }

        [JsonProperty("lineCap")]
        public string LineCap { get; set; }
    }
}
