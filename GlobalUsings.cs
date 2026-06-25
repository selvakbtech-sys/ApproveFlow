// Global using declarations — available in every .cs file in the project.
// ImplicitUsings already covers: System, System.IO, System.Linq, System.Collections.Generic,
// System.Threading.Tasks, Microsoft.AspNetCore.Http, Microsoft.Extensions.Configuration,
// Microsoft.Extensions.Logging, Microsoft.AspNetCore.Hosting, etc.
// This file adds the remaining project-specific and framework namespaces.

global using System.ComponentModel.DataAnnotations;
global using System.ComponentModel.DataAnnotations.Schema;
global using System.Security.Claims;

global using ApproveFlow.MVC.Data;
global using ApproveFlow.MVC.Models.Domain;
global using ApproveFlow.MVC.Models.ViewModels;
global using ApproveFlow.MVC.Services;

global using Microsoft.AspNetCore.Authentication;
global using Microsoft.AspNetCore.Authentication.Cookies;
global using Microsoft.AspNetCore.Authorization;
global using Microsoft.AspNetCore.Mvc;
global using Microsoft.AspNetCore.Mvc.Rendering;
global using Microsoft.EntityFrameworkCore;
