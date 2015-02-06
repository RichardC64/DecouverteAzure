using System.ComponentModel.DataAnnotations;

namespace WebDecouverteAzure.Models
{
    public class ParametrageModel
    {
        [Required(ErrorMessage = "*")]
        public int Duration { get; set; }

        public string Result { get; set; }
    }
}
