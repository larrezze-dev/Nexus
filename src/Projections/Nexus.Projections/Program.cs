// nexus-projections (conventions-events.md §8): Kafka -> ClickHouse projections.
//   nexus-projections [run]                  worker (ClickHouse migrations, then one consumer per projection, /health)
//   nexus-projections rebuild <projection>   truncate + replay one projection, then exit 0 / non-zero
// The projections themselves are registered in ProjectionsCli.RegisterProjections (single place).
return await Nexus.Projections.ProjectionsCli.RunAsync(args);
