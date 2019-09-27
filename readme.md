# ApplicationInsights.AWS [![Build status](https://ci.appveyor.com/api/projects/status/58v5el4i3hf4dya5?svg=true)](https://ci.appveyor.com/project/hermanho/applicationinsights-aws) [![Build Status](https://travis-ci.com/hermanho/ApplicationInsights.AWS.svg?branch=master)](https://travis-ci.com/hermanho/ApplicationInsights.AWS) [![NuGet](https://img.shields.io/nuget/vpre/ApplicationInsights.AWS.svg)](https://www.nuget.org/packages/ApplicationInsights.AWS/) 

When you have enable Application Insights, all external http requests are recorded. However, those AWS requests are recorded without payloads because the parameters are transferred in different way.

<img src="images/before.png" height="500">


This library will extract the payloads and show you in Application Insights

<img src="images/after.png" height="500">
<img src="images/after2.png">

It also group AWS requests in Application map

<img src="images/map1.png" height="300">

## Install
Install from Nuget and search "ApplicationInsights.AWS"


## Setup
Add "AddAWSInjection()" in ConfigureServices
```
public void ConfigureServices(IServiceCollection services)
{
    services.AddApplicationInsightsTelemetry();
    services.AddAWSInjection();
}
```
