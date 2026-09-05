namespace AiRouter.Providers;

public interface IAiProviderFactory
{
    bool CanCreate(ProviderDefinition definition);
    IAiProvider Create(ProviderDefinition definition);
}
