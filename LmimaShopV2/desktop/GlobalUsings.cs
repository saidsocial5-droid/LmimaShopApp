// Global usings shared by the whole desktop project.
// The WPF SDK does not provide the full default implicit using set, and every data
// layer file composes Dapper commands against MySqlConnector connections.
global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
global using System.Net.Http;
global using System.Threading;
global using System.Threading.Tasks;
global using Dapper;
global using Microsoft.Extensions.DependencyInjection;
