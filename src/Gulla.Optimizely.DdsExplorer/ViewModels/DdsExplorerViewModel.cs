namespace Gulla.Optimizely.DdsExplorer.ViewModels
{
    public class DdsExplorerViewModel
    {
        /// <summary>The store shown on the store page; null on the store list.</summary>
        public string StoreName { get; set; }

        public bool IsSystemStore { get; set; }

        public string AntiforgeryHeaderName { get; set; }

        public string AntiforgeryToken { get; set; }
    }
}
