namespace AssetForge.Service.Health
{
    public sealed class ReadinessState
    {
        private volatile bool _isReady = true;
        public bool IsReady => _isReady;
        public void MarkNotReady() => _isReady = false;
    }
}
