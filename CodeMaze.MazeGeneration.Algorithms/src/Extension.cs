using CodeMaze.MazeGeneration.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using CodeMaze.MazeGeneration.Algorithms.RecursiveBacktracker;

namespace CodeMaze.MazeGeneration.Generator;

public static class MazeGenerationServiceCollectionExtensions
{
    public static IServiceCollection AddMazeGeneration(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MazeGenerationOptions>(
            configuration.GetSection(MazeGenerationOptions.SectionName));

        // The names here are what appsettings.json refers to
        services.AddKeyedSingleton<IGeneratorAlgorithm, RecursiveBacktracker>("RecursiveBacktracker");
        //services.AddKeyedSingleton<IGeneratorAlgorithm, PrimsAlgorithm>("Prims");
        //services.AddKeyedSingleton<IGeneratorAlgorithm, KruskalsAlgorithm>("Kruskals");

        services.AddSingleton<IGeneratorMappingStore, ConfigGeneratorMappingStore>();
        services.AddSingleton<IMazeGenerator, MazeGenerator>();
        return services;
    }
}