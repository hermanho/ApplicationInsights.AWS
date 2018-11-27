using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace ApplicationInsights.AWS
{
    public class SimpleTypeContractResolver : DefaultContractResolver
    {
        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            var property = base.CreateProperty(member, memberSerialization);

            var propertyType = property.PropertyType;
            if (propertyType.IsPrimitive
                || propertyType.IsValueType
                || propertyType == typeof(decimal)
                || propertyType == typeof(string) 
                || propertyType == typeof(DateTime)
                || propertyType == typeof(DateTimeOffset)
                || propertyType == typeof(TimeSpan))

            {
                property.ShouldSerialize = instance => true;
            }
            else
            {
                property.ShouldSerialize = instance => false;
            }
            return property;
        }
    }
}
