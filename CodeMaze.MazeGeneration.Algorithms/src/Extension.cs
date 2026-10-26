using CodeMaze.MazeGeneration.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using CodeMaze.MazeGeneration.Algorithms.RecursiveBacktracker;
using CodeMaze.MazeGeneration.Algorithms.Prims;
using CodeMaze.MazeGeneration.Algorithms.Kruskals;
using CodeMaze.MazeGeneration.Algorithms.Ellers;
using CodeMaze.MazeGeneration.Algorithms.RecursiveDivision;
using CodeMaze.MazeGeneration.Algorithms.Sidewinder;
using CodeMaze.MazeGeneration.Algorithms.Wilsons;

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
        services.AddKeyedSingleton<IGeneratorAlgorithm, Prims>("Prims");
        services.AddKeyedSingleton<IGeneratorAlgorithm, Kruskals>("Kruskals");
        services.AddKeyedSingleton<IGeneratorAlgorithm, Ellers>("Ellers");
        services.AddKeyedSingleton<IGeneratorAlgorithm, RecursiveDivision>("RecursiveDivision");
        services.AddKeyedSingleton<IGeneratorAlgorithm, Sidewinder>("Sidewinder");
        services.AddKeyedSingleton<IGeneratorAlgorithm, Wilsons>("Wilsons");

        services.AddSingleton<IGeneratorMappingStore, ConfigGeneratorMappingStore>();
        services.AddSingleton<IMazeGenerator, MazeGenerator>();
        return services;
    }
}