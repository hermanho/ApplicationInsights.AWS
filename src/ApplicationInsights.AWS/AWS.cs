using Amazon.Runtime;
using Amazon.Runtime.Internal;
using Amazon.Runtime.Internal.Transform;
using Amazon.XRay.Recorder.Core.Internal.Utils;
using Amazon.XRay.Recorder.Handlers.AwsSdk.Entities;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ApplicationInsights.AWS
{
    //https://github.com/aws/aws-xray-sdk-dotnet/blob/5aa148b5167ac2b63759efeafc641e5a0e0718c9/sdk/src/Handlers/AwsSdk/Internal/XRayPipelineHandler.cs#L1
    public class ApplicationInsightsPipelineHandler : PipelineHandler
    {
        private const string DefaultAwsWhitelistManifestResourceName = "ApplicationInsights.AWS.DefaultAWSWhitelist.json";
        private const string S3RequestIdHeaderKey = "x-amz-request-id";
        private const string S3ExtendedRequestIdHeaderKey = "x-amz-id-2";
        private const string ExtendedRquestIdSegmentKey = "id_2";

        /// <summary>
        /// Gets AWS service manifest of operation parameter whitelist.
        /// </summary>
        public AWSServiceHandlerManifest AWSServiceHandlerManifest { get; private set; }

        private TelemetryClient _telemetryClient;
        private ILogger<ApplicationInsightsPipelineHandler> _logger;
        private ApplicationInsightsPipelineOption _options;
        private JsonSerializerSettings _jsonSerializerSettings;

        /// <summary>
        /// Initializes a new instance of the <see cref="ApplicationInsightsPipelineHandler" /> class.
        /// </summary>
        public ApplicationInsightsPipelineHandler(ILogger<ApplicationInsightsPipelineHandler> logger,
            TelemetryClient telemetryClient,
            IOptions<ApplicationInsightsPipelineOption> options)
        {
            _telemetryClient = telemetryClient;
            _logger = logger;
            _options = options.Value;

            if (string.IsNullOrEmpty(_options.Path))
            {
                _logger.LogDebug("The path is null or empty, initializing with default AWS whitelist.");
                InitWithDefaultAWSWhitelist();
            }
            else
            {
                using (Stream stream = new FileStream(_options.Path, FileMode.Open, FileAccess.Read))
                {
                    Init(stream);
                }
            }

            _jsonSerializerSettings = new JsonSerializerSettings();
            _jsonSerializerSettings.ContractResolver = new SimpleTypeContractResolver();
            _jsonSerializerSettings.DefaultValueHandling = DefaultValueHandling.Ignore;
            _jsonSerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
        }

        private bool TryReadPropertyValue(object obj, string propertyName, out object value)
        {
            value = 0;

            try
            {
                if (obj == null || propertyName == null)
                {
                    return false;
                }

                var property = obj.GetType().GetProperty(propertyName);

                if (property == null)
                {
                    _logger.LogDebug("Property doesn't exist. {0}", propertyName);
                    return false;
                }

                value = property.GetValue(obj);
                return true;
            }
            catch (ArgumentNullException e)
            {
                _logger.LogError(e, "Failed to read property because argument is null.");
                return false;
            }
            catch (AmbiguousMatchException e)
            {
                _logger.LogError(e, "Failed to read property because of duplicate property name.");
                return false;
            }
        }

        /// <summary>
        /// Removes amazon prefix from service name. There are two type of service name.
        ///     Amazon.DynamoDbV2
        ///     AmazonS3
        /// </summary>
        /// <param name="serviceName">Name of the service.</param>
        /// <returns>String after removing Amazon prefix.</returns>
        private static string RemoveAmazonPrefixFromServiceName(string serviceName)
        {
            return RemovePrefix(RemovePrefix(serviceName, "Amazon"), ".");
        }

        private static string RemovePrefix(string originalString, string prefix)
        {
            if (prefix == null)
            {
                throw new ArgumentNullException("prefix");
            }

            if (originalString == null)
            {
                throw new ArgumentNullException("originalString");
            }

            if (originalString.StartsWith(prefix))
            {
                return originalString.Substring(prefix.Length);
            }

            return originalString;
        }

        private static string RemoveSuffix(string originalString, string suffix)
        {
            if (suffix == null)
            {
                throw new ArgumentNullException("suffix");
            }

            if (originalString == null)
            {
                throw new ArgumentNullException("originalString");
            }

            if (originalString.EndsWith(suffix))
            {
                return originalString.Substring(0, originalString.Length - suffix.Length);
            }

            return originalString;
        }

        private void AddMapKeyProperty(IDictionary<string, string> aws, object obj, string propertyName, string renameTo = null)
        {
            if (!TryReadPropertyValue(obj, propertyName, out object propertyValue))
            {
                _logger.LogDebug("Failed to read property value: {0}", propertyName);
                return;
            }

            var dictionaryValue = propertyValue as IDictionary;

            if (dictionaryValue == null)
            {
                _logger.LogDebug("Property value does not implements IDictionary: {0}", propertyName);
                return;
            }

            var newPropertyName = string.IsNullOrEmpty(renameTo) ? propertyName : renameTo;
            aws[newPropertyName.FromCamelCaseToSnakeCase()] = dictionaryValue.Keys.ToString();
        }

        private void AddListLengthProperty(IDictionary<string, string> aws, object obj, string propertyName, string renameTo = null)
        {
            if (!TryReadPropertyValue(obj, propertyName, out object propertyValue))
            {
                _logger.LogDebug("Failed to read property value: {0}", propertyName);
                return;
            }

            var listValue = propertyValue as IList;

            if (listValue == null)
            {
                _logger.LogDebug("Property value does not implements IList: {0}", propertyName);
                return;
            }

            var newPropertyName = string.IsNullOrEmpty(renameTo) ? propertyName : renameTo;
            aws[newPropertyName.FromCamelCaseToSnakeCase()] = listValue.Count.ToString();
        }

        private void InitWithDefaultAWSWhitelist()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(DefaultAwsWhitelistManifestResourceName))
            {
                Init(stream);
            }
        }

        private void Init(Stream stream)
        {
            using (var reader = new StreamReader(stream))
            {
                try
                {
                    using (var jsonTextReader = new JsonTextReader(reader))
                    {
                        var serializer = new JsonSerializer();
                        AWSServiceHandlerManifest = serializer.Deserialize<AWSServiceHandlerManifest>(jsonTextReader);
                    }
                }
                catch (JsonException e)
                {
                    _logger.LogError(e, "Failed to load AWSServiceHandlerManifest.");
                }
            }
        }

        /// <summary>
        /// Processes Begin request by starting subsegment.
        /// </summary>
        private IOperationHolder<DependencyTelemetry> ProcessBeginRequest(IExecutionContext executionContext)
        {
            var request = executionContext.RequestContext.Request;

            //_recorder.BeginSubsegment(RemoveAmazonPrefixFromServiceName(request.ServiceName));

            var operationHolder = _telemetryClient.StartOperation<DependencyTelemetry>(request.ServiceName);
            operationHolder.Telemetry.Type = "AWS";
            try
            {
                //operationHolder.Telemetry.Data = JsonConvert.SerializeObject(request.Parameters);
                operationHolder.Telemetry.Data = JsonConvert.SerializeObject(request.OriginalRequest, _jsonSerializerSettings);
            }
            catch
            { }
            return operationHolder;
        }

        /// <summary>
        /// Processes End request by ending subsegment.
        /// </summary>
        private void ProcessEndRequest(IOperationHolder<DependencyTelemetry> operationHolder, IExecutionContext executionContext)
        {
            var responseContext = executionContext.ResponseContext;
            var requestContext = executionContext.RequestContext;
            try
            {
                if (responseContext == null)
                {
                    _logger.LogDebug("Failed to handle AfterResponseEvent, because response is null.");
                    return;
                }

                var client = executionContext.RequestContext.ClientConfig;
                if (client == null)
                {
                    _logger.LogDebug("Failed to handle AfterResponseEvent, because client from the Response Context is null");
                    return;
                }

                var serviceName = RemoveAmazonPrefixFromServiceName(requestContext.Request.ServiceName);
                var operation = RemoveSuffix(requestContext.OriginalRequest.GetType().Name, "Request");

                operationHolder.Telemetry.Properties["region"] = client.RegionEndpoint?.SystemName;
                operationHolder.Telemetry.Properties["operation"] = operation;
                if (responseContext.Response == null)
                {
                    if (requestContext.Request.Headers.TryGetValue("x-amzn-RequestId", out string requestId))
                    {
                        operationHolder.Telemetry.Properties["request_id"] = requestId;
                    }
                    // s3 doesn't follow request header id convention
                    else
                    {
                        if (requestContext.Request.Headers.TryGetValue(S3RequestIdHeaderKey, out requestId))
                        {
                            operationHolder.Telemetry.Properties["request_id"] = requestId;
                        }

                        if (requestContext.Request.Headers.TryGetValue(S3ExtendedRequestIdHeaderKey, out requestId))
                        {
                            operationHolder.Telemetry.Properties[ExtendedRquestIdSegmentKey] = requestId;
                        }
                    }
                }
                else
                {
                    operationHolder.Telemetry.Properties["request_id"] = responseContext.Response.ResponseMetadata.RequestId;
                    operationHolder.Telemetry.Properties["request_id"] = responseContext.Response.ResponseMetadata.RequestId;
                    AddResponseSpecificInformation(serviceName, operation, responseContext.Response, operationHolder.Telemetry.Properties);
                }

                if (responseContext.HttpResponse != null)
                {
                    AddHttpInformation(operationHolder.Telemetry, responseContext.HttpResponse);
                }

                AddRequestSpecificInformation(serviceName, operation, requestContext.OriginalRequest, operationHolder.Telemetry.Properties);
            }
            catch { }
            _telemetryClient.StopOperation(operationHolder);
        }

        private void AddHttpInformation(DependencyTelemetry telemetry, IWebResponseData httpResponse)
        {
            var responseAttributes = new Dictionary<string, object>();
            int statusCode = (int)httpResponse.StatusCode;
            if (statusCode >= 400 && statusCode <= 499)
            {
                telemetry.Success = false;
                if (statusCode == 429)
                {
                    telemetry.Properties["Throttle"] = "true";
                }
            }
            else if (statusCode >= 500 && statusCode <= 599)
            {
                telemetry.Success = false;
            }

            responseAttributes["status"] = statusCode;
            responseAttributes["content_length"] = httpResponse.ContentLength;
            telemetry.Properties["aws http response"] = JsonConvert.SerializeObject(responseAttributes);
        }

        private void ProcessException(DependencyTelemetry telemetry, AmazonServiceException ex)
        {
            int statusCode = (int)ex.StatusCode;
            var responseAttributes = new Dictionary<string, object>();

            if (statusCode >= 400 && statusCode <= 499)
            {
                telemetry.Success = false;
                if (statusCode == 429)
                {
                    telemetry.Properties["Throttle"] = "true";
                }
            }
            else if (statusCode >= 500 && statusCode <= 599)
            {
                telemetry.Success = false;
            }

            responseAttributes["status"] = statusCode;
            telemetry.Properties["aws http response"] = JsonConvert.SerializeObject(responseAttributes);

            telemetry.Properties["request_id"] = ex.RequestId;
        }

        private void AddRequestSpecificInformation(string serviceName, string operation, AmazonWebServiceRequest request, IDictionary<string, string> aws)
        {
            if (serviceName == null)
            {
                throw new ArgumentNullException("serviceName");
            }

            if (operation == null)
            {
                throw new ArgumentNullException("operation");
            }

            if (request == null)
            {
                throw new ArgumentNullException("request");
            }

            if (aws == null)
            {
                throw new ArgumentNullException("aws");
            }

            if (AWSServiceHandlerManifest == null)
            {
                _logger.LogDebug("AWSServiceHandlerManifest doesn't exist.");
                return;
            }

            if (!AWSServiceHandlerManifest.Services.TryGetValue(serviceName, out AWSServiceHandler serviceHandler))
            {
                _logger.LogDebug("Service name doesn't exist in AWSServiceHandlerManifest: serviceName = {0}.", serviceName);
                return;
            }

            if (!serviceHandler.Operations.TryGetValue(operation, out AWSOperationHandler operationHandler))
            {
                _logger.LogDebug("Operation doesn't exist in AwsServiceInfo: serviceName = {0}, operation = {1}.", serviceName, operation);
                return;
            }

            if (operationHandler.RequestParameters != null)
            {
                foreach (string parameter in operationHandler.RequestParameters)
                {
                    if (TryReadPropertyValue(request, parameter, out object propertyValue))
                    {
                        aws[parameter.FromCamelCaseToSnakeCase()] = propertyValue.ToString();
                    }
                }
            }

            if (operationHandler.RequestDescriptors != null)
            {
                foreach (KeyValuePair<string, AWSOperationRequestDescriptor> kv in operationHandler.RequestDescriptors)
                {
                    var propertyName = kv.Key;
                    var descriptor = kv.Value;

                    if (descriptor.Map && descriptor.GetKeys)
                    {
                        AddMapKeyProperty(aws, request, propertyName, descriptor.RenameTo);
                    }
                    else if (descriptor.List && descriptor.GetCount)
                    {
                        AddListLengthProperty(aws, request, propertyName, descriptor.RenameTo);
                    }
                }
            }
        }

        private void AddResponseSpecificInformation(string serviceName, string operation, AmazonWebServiceResponse response, IDictionary<string, string> aws)
        {
            if (serviceName == null)
            {
                throw new ArgumentNullException("serviceName");
            }

            if (operation == null)
            {
                throw new ArgumentNullException("operation");
            }

            if (response == null)
            {
                throw new ArgumentNullException("response");
            }

            if (aws == null)
            {
                throw new ArgumentNullException("aws");
            }

            if (AWSServiceHandlerManifest == null)
            {
                _logger.LogDebug("AWSServiceHandlerManifest doesn't exist.");
                return;
            }

            if (!AWSServiceHandlerManifest.Services.TryGetValue(serviceName, out AWSServiceHandler serviceHandler))
            {
                _logger.LogDebug("Service name doesn't exist in AWSServiceHandlerManifest: serviceName = {0}.", serviceName);
                return;
            }

            if (!serviceHandler.Operations.TryGetValue(operation, out AWSOperationHandler operationHandler))
            {
                _logger.LogDebug("Operation doesn't exist in AwsServiceInfo: serviceName = {0}, operation = {1}.", serviceName, operation);
                return;
            }

            if (operationHandler.ResponseParameters != null)
            {
                foreach (string parameter in operationHandler.ResponseParameters)
                {
                    if (TryReadPropertyValue(response, parameter, out object propertyValue))
                    {
                        aws[parameter.FromCamelCaseToSnakeCase()] = propertyValue.ToString();
                    }
                }
            }

            if (operationHandler.ResponseDescriptors != null)
            {
                foreach (KeyValuePair<string, AWSOperationResponseDescriptor> kv in operationHandler.ResponseDescriptors)
                {
                    var propertyName = kv.Key;
                    var descriptor = kv.Value;

                    if (descriptor.Map && descriptor.GetKeys)
                    {
                        AddMapKeyProperty(aws, response, propertyName, descriptor.RenameTo);
                    }
                    else if (descriptor.List && descriptor.GetCount)
                    {
                        AddListLengthProperty(aws, response, propertyName, descriptor.RenameTo);
                    }
                }
            }
        }

        public override void InvokeSync(IExecutionContext executionContext)
        {
            if (ExcludeServiceOperation(executionContext))
            {
                base.InvokeSync(executionContext);
            }
            else
            {
                var operationHolder = ProcessBeginRequest(executionContext);

                try
                {
                    base.InvokeSync(executionContext);
                }

                catch (Exception e)
                {
                    _telemetryClient.TrackException(e);

                    if (e is AmazonServiceException amazonServiceException)
                    {
                        ProcessException(operationHolder.Telemetry, amazonServiceException);
                    }

                    throw;
                }

                finally
                {
                    ProcessEndRequest(operationHolder, executionContext);
                }
            }
        }

        //https://github.com/aws/aws-xray-sdk-dotnet/blob/994c27f173a53060398d27f80fbe4f101d2497aa/sdk/src/Handlers/AwsSdk/Internal/AWSXRaySDKUtils.cs
        private static readonly string XRayServiceName = "XRay";
        private static readonly ISet<string> WhitelistedOperations = new HashSet<string> { "GetSamplingRules", "GetSamplingTargets" };
        internal static bool IsBlacklistedOperation(string serviceName, string operation)
        {
            if (string.Equals(serviceName, XRayServiceName) && WhitelistedOperations.Contains(operation))
            {
                return true;
            }
            return false;
        }

        private bool ExcludeServiceOperation(IExecutionContext executionContext)
        {
            var requestContext = executionContext.RequestContext;
            var serviceName = RemoveAmazonPrefixFromServiceName(requestContext.Request.ServiceName);
            var operation = RemoveSuffix(requestContext.OriginalRequest.GetType().Name, "Request");

            return IsBlacklistedOperation(serviceName, operation);
        }

        /// <summary>
        /// Process Asynchronous <see cref="AmazonServiceClient"/> operations. A subsegment is started at the beginning of 
        /// the request and ended at the end of the request.
        /// </summary>
        public override async Task<T> InvokeAsync<T>(IExecutionContext executionContext)
        {
            T ret = null;

            if (ExcludeServiceOperation(executionContext))
            {
                ret = await base.InvokeAsync<T>(executionContext).ConfigureAwait(false);
            }
            else
            {
                var operationHolder = ProcessBeginRequest(executionContext);

                try
                {
                    ret = await base.InvokeAsync<T>(executionContext).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    _telemetryClient.TrackException(e);

                    if (e is AmazonServiceException amazonServiceException)
                    {
                        ProcessException(operationHolder.Telemetry, amazonServiceException);
                    }

                    throw;
                }

                finally
                {
                    ProcessEndRequest(operationHolder, executionContext);
                }

            }

            return ret;
        }
    }

    public class ApplicationInsightsPipelineOption
    {
        public bool RegisterAll { get; set; }
        public string Path { get; set; }
    }

    //https://github.com/aws/aws-xray-sdk-dotnet/blob/5aa148b5167ac2b63759efeafc641e5a0e0718c9/sdk/src/Handlers/AwsSdk/Internal/XRayPipelineHandler.cs#L621
    public class ApplicationInsightsPipelineCustomizer : IRuntimePipelineCustomizer
    {
        public string UniqueName { get { return "X-Ray Registration Customization"; } }

        private List<Type> types = new List<Type>();
        private ReaderWriterLockSlim rwLock = new ReaderWriterLockSlim();

        public IServiceProvider _serviceProvider { get; private set; }
        public ApplicationInsightsPipelineOption _option { get; private set; }

        public ApplicationInsightsPipelineCustomizer(IServiceProvider serviceProvider, IOptions<ApplicationInsightsPipelineOption> options)
        {
            _serviceProvider = serviceProvider;
            _option = options.Value;
        }

        public void Customize(Type serviceClientType, RuntimePipeline pipeline)
        {
            if (serviceClientType.BaseType != typeof(AmazonServiceClient))
                return;

            bool addCustomization = _option.RegisterAll;

            if (!addCustomization)
            {
                addCustomization = ProcessType(serviceClientType, addCustomization);
            }

            var handler = _serviceProvider.GetRequiredService<ApplicationInsightsPipelineHandler>();

            pipeline.AddHandlerAfter<EndpointResolver>(handler);
        }

        private bool ProcessType(Type serviceClientType, bool addCustomization)
        {
            rwLock.EnterReadLock();

            try
            {
                foreach (var registeredType in types)
                {
                    if (registeredType.IsAssignableFrom(serviceClientType))
                    {
                        addCustomization = true;
                        break;
                    }
                }
            }
            finally
            {
                rwLock.ExitReadLock();
            }

            return addCustomization;
        }

        /// <summary>
        /// Adds type to the list of <see cref="Type" />.
        /// </summary>
        /// <param name="type"> Type of <see cref="Runtime.AmazonServiceClient"/> to be registered with X-Ray.</param>
        public void AddType(Type type)
        {
            rwLock.EnterWriteLock();

            try
            {
                types.Add(type);
            }
            finally
            {
                rwLock.ExitWriteLock();
            }
        }
    }
}
