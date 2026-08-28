using System.Text.Json.Serialization;

using BackingTrack.Api.Dtos;

using Microsoft.AspNetCore.Mvc;

namespace BackingTrack.Api;

[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(ValidationProblemDetails))]
[JsonSerializable(typeof(GenerationJobDto))]
[JsonSerializable(typeof(GenerationRequestDto))]
internal partial class AppJsonSerializerContext : JsonSerializerContext;