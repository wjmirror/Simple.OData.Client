namespace Simple.OData.Client
{
    internal class ExpressionContext
    {
        public ISession Session { get; set; }
        public EntityCollection EntityCollection { get; set; }
        public string DynamicPropertiesContainerName { get; set; }

        public ExpressionContext(ISession session, EntityCollection entityCollection = null, string dynamicPropertiesContainerName = null)
        {
            this.Session = session;
            this.EntityCollection = entityCollection;
            this.DynamicPropertiesContainerName = dynamicPropertiesContainerName;
        }
    }
}
