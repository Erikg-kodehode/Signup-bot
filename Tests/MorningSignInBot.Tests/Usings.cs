// System namespaces
global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Threading;
global using System.Threading.Tasks;
global using System.ComponentModel.DataAnnotations;

// Microsoft namespaces
global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.Options;
global using Microsoft.EntityFrameworkCore;
global using Microsoft.Extensions.DependencyInjection;

// Discord namespaces
global using Discord;
global using Discord.WebSocket;
global using Discord.Interactions;

// Project namespaces
global using MorningSignInBot.Configuration;
global using MorningSignInBot.Services;
global using MorningSignInBot.Data;
global using PublicHoliday;

// Test framework - import directly from assemblies
global using Xunit;
global using Xunit.Abstractions;
global using Moq;
global using Moq.Protected;

