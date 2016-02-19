using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Horizon;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Diagnostics.Contracts;

namespace Horizon.Forge
{
    static class DynamicTypeWrapBuilder
    {
        const MethodAttributes CtorAttributes = MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName | MethodAttributes.Public;
        const TypeAttributes WrapperClassAttributes = TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed | TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit;
        const FieldAttributes InstaceFieldAttributes = FieldAttributes.Private | FieldAttributes.InitOnly;
        const MethodAttributes GetterSetterAttributes = MethodAttributes.Public | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual;

        static readonly Dictionary<string, Type> _typeCache = new Dictionary<string, Type>();
        static readonly Dictionary<string, Delegate> _internalWrapperCache = new Dictionary<string, Delegate>();

        internal static dynamic CreateTypeInstance(string typeName, object instance)
        {
            Type type;
            if (!_typeCache.TryGetValue(typeName, out type))
                throw new NotSupportedException("Unknown Horizon Wrapper type.");


            // Now we have our type. Let's create an instance from it:

            var actualType = instance.GetType();

            dynamic result;
            if (actualType.IsPublic)
            {
                result = Info.Create(type, instance);
            }
            else {
                Delegate @delegate;

                if (!_internalWrapperCache.TryGetValue(typeName, out @delegate))
                {
                    var method = new DynamicMethod("Create_" + typeName, type, new[] { actualType }, type.Module, skipVisibility: true);

                    var generator = method.GetILGenerator();
                    generator.Emit(OpCodes.Ldarg_0);
                    generator.Emit(OpCodes.Newobj, type.GetConstructors().First());
                    generator.Emit(OpCodes.Ret);

                    _internalWrapperCache[typeName] = @delegate = method.Build();
                }

                result = @delegate.DynamicInvoke(instance);
            }

            return result;
        }

        //TODO: Refactor to membernodes
        //TODO: Allow multiple interfaces
        //TODO: Check for internal methods / calls on the actual type. The generated IL code will not be able to call them.
        //TODO: Support parent class
        public static Type CreateWrapper(string typeName, Type interfaceWrapperType, Type actualType, bool throwNotSupported)
        {
            Contract.Ensures(Contract.Result<Type>() != null);
            Type type;
            if (_typeCache.TryGetValue(typeName, out type))
                return type;

            var builder = DynamicTypeBuilder._HorizonModule.DefineType(typeName, WrapperClassAttributes);
            builder.AddInterfaceImplementation(interfaceWrapperType);

            var instanceField = builder.DefineField("_instance", actualType, InstaceFieldAttributes);

            var ctor = builder.DefineConstructor(CtorAttributes, CallingConventions.Standard, new[] { actualType });

            var ctorGenerator = ctor.GetILGenerator();

            var jumpLabel = ctorGenerator.DefineLabel();

            ctorGenerator.Emit(OpCodes.Ldarg_0);
            ctorGenerator.Emit(OpCodes.Call, Info<object>.Extended.DefaultConstructor.ConstructorInfo);
            ctorGenerator.Emit(OpCodes.Ldarg_1);
            ctorGenerator.Emit(OpCodes.Brtrue_S, jumpLabel);

            ctorGenerator.Emit(OpCodes.Ldstr, "instance");
            ctorGenerator.Emit(OpCodes.Newobj, Info<ArgumentNullException>.GetConstructor(typeof(string)).ConstructorInfo);
            ctorGenerator.Emit(OpCodes.Throw);

            ctorGenerator.MarkLabel(jumpLabel);
            ctorGenerator.Emit(OpCodes.Ldarg_0);
            ctorGenerator.Emit(OpCodes.Ldarg_1);
            ctorGenerator.Emit(OpCodes.Stfld, instanceField);
            ctorGenerator.Emit(OpCodes.Ret);

            var actualMethods = Info.Extended.Methods(actualType).ToLookup(x => x.Name);

            foreach (var methodCaller in Info.Extended.Methods(interfaceWrapperType))
            {
                MethodInfo method = methodCaller.MethodInfo;

                var parameterTypes = methodCaller.ParameterTypes.Select(x => x.ParameterType).ToArray();

                var methodBuilder = builder.DefineMethod(method.Name, GetterSetterAttributes, method.ReturnType, parameterTypes);

                var methodGenerator = methodBuilder.GetILGenerator();

                methodGenerator.Emit(OpCodes.Ldarg_0);
                methodGenerator.Emit(OpCodes.Ldfld, instanceField);

                var relevantMethods = actualMethods[method.Name];
                var actualMethod = Info.Extended.ResolveSpecificCaller(relevantMethods, parameterTypes, throwWhenNotfound: false);

                if (actualMethod != null)
                {
                    if (actualMethod.MethodInfo.IsPublic && actualType.IsPublic)
                    {
                        for (int i = 1; i <= parameterTypes.Length; i++)
                            methodGenerator.Emit(OpCodes.Ldarg_S, i);

                        methodGenerator.Emit(OpCodes.Call, actualMethod.MethodInfo);
                        methodGenerator.Emit(OpCodes.Ret);
                    }
                    else
                    {
                        //TODO: Support this case;
                        throw new NotSupportedException();
                    }
                }
                else if (throwNotSupported)
                {
                    ThrowNotSupportedException(methodGenerator);
                }
                else
                {
                    EmptyMethodMemberDefinition.Build(methodCaller.ReturnType, methodGenerator);
                }
            }

            var actualProperties = actualType.GetProperties().ToDictionary(x => x.Name);
            foreach (var propertyCaller in Info.Extended.Properties(interfaceWrapperType))
            {
                var property = propertyCaller.PropertyInfo;

                var propertyBuilder = builder.DefineProperty(propertyCaller.Name, PropertyAttributes.SpecialName, propertyCaller.MemberType, property.GetIndexParameters().Select(x => x.ParameterType).ToArray());

                PropertyInfo actualProperty;
                actualProperties.TryGetValue(propertyCaller.Name, out actualProperty);

                if (property.CanRead)
                {
                    var getBuilder = builder.DefineMethod("get_" + propertyCaller.Name, GetterSetterAttributes, property.PropertyType, Type.EmptyTypes);

                    var getIL = getBuilder.GetILGenerator();

                    if (actualProperty == null || !actualProperty.CanRead)
                    {
                        GenerateUnknownProperty(getIL, propertyCaller, throwNotSupported);
                    }
                    else
                    {
                        var getter = actualProperty.GetMethod;
                        
                        getIL.Emit(OpCodes.Ldarg_0);
                        getIL.Emit(OpCodes.Ldfld, instanceField);

                        if (getter.IsPublic && actualType.IsPublic)
                        {
                            getIL.Emit(OpCodes.Callvirt, getter);
                        }
                        else
                        {
                            getIL.Emit(OpCodes.Ldstr, InternalTypeHelper.Create(actualType));
                            getIL.Emit(OpCodes.Ldstr, property.Name);
                            getIL.Emit(OpCodes.Call, InternalTypeHelper.GetInternalPropertyMethod);

                            if (property.PropertyType.IsValueType)
                            {
                                getIL.Emit(OpCodes.Unbox_Any, property.PropertyType);
                            }
                            else
                            {
                                getIL.Emit(OpCodes.Castclass, property.PropertyType);
                            }
                        }

                        getIL.Emit(OpCodes.Ret);
                    }

                    propertyBuilder.SetGetMethod(getBuilder);
                }

                if (property.CanWrite)
                {
                    var setBuilder = builder.DefineMethod("set_" + propertyCaller.Name, GetterSetterAttributes, typeof(void), new[] { property.PropertyType });

                    var setIL = setBuilder.GetILGenerator();

                    if (actualProperty == null || !actualProperty.CanWrite)
                    {
                        GenerateUnknownProperty(setIL, propertyCaller, throwNotSupported);
                    }
                    else
                    {
                        var setter = actualProperty.SetMethod;
                        var isPublic = setter.IsPublic && actualType.IsPublic;

                        setIL.Emit(OpCodes.Ldarg_0);                        
                        setIL.Emit(OpCodes.Ldfld, instanceField);

                        if (!isPublic)
                        {
                            setIL.Emit(OpCodes.Ldstr, InternalTypeHelper.Create(actualType));
                            setIL.Emit(OpCodes.Ldstr, property.Name);
                        }

                        setIL.Emit(OpCodes.Ldarg_1);

                        if (isPublic)
                        {
                            setIL.Emit(OpCodes.Callvirt, actualProperty.SetMethod);
                        }
                        else
                        {
                            setIL.Emit(OpCodes.Call, InternalTypeHelper.SetInternalPropertyMethod);
                        }

                        setIL.Emit(OpCodes.Ret);
                    }

                    propertyBuilder.SetSetMethod(setBuilder);
                }
            }

            _typeCache[typeName] = type = builder.CreateType();

            return type;
        }


        static void GenerateUnknownProperty(ILGenerator generator, IPropertyCaller propertyCaller, bool throwNotImplemented)
        {
            if (throwNotImplemented)
            {
                ThrowNotSupportedException(generator);
            }
            else
            {
                EmptyMethodMemberDefinition.Build(propertyCaller.MemberType, generator);
            }
        }

        static void ThrowNotSupportedException(ILGenerator generator)
        {
            var caller = Info<NotSupportedException>.Extended.DefaultConstructor;
            generator.Emit(OpCodes.Newobj, caller.ConstructorInfo);
            generator.Emit(OpCodes.Throw);
        }
    }

    
    public class InternalTypeHelper 
    {
        public static readonly MethodInfo GetInternalPropertyMethod = typeof(InternalTypeHelper).GetMethod("GetInternalProperty");
        public static readonly MethodInfo SetInternalPropertyMethod = typeof(InternalTypeHelper).GetMethod("SetInternalProperty");
        public static readonly MethodInfo CallMethodMethod = typeof(InternalTypeHelper).GetMethod("CallMethod");

        static readonly Dictionary<string, InternalTypeHelper> _cache = new Dictionary<string, InternalTypeHelper>();

        public static string Create(Type type)
        {
            var value = type.ToString();
            if (_cache.ContainsKey(value))
                return value;

            var helper = new InternalTypeHelper(type, value);
            _cache[value] = helper;
            return value;
        }


        private InternalTypeHelper(Type actualType, string actualTypeString)
        {
            _actualType = actualType;
            _actualTypeString = actualTypeString;
        }

        public static object GetInternalProperty(object instance, string idx, string method)
        {
            return _cache[idx].InternalGet(instance, method);
        }

        public static void SetInternalProperty(object instance, string idx, string method, object value)
        {
            _cache[idx].InternalSet(instance, method, value);
        }

        readonly Dictionary<string, Func<object, object>> _get = new Dictionary<string, Func<object, object>>();
        readonly Dictionary<string, Action<object, object>> _set = new Dictionary<string, Action<object, object>>();

        private readonly Type _actualType;
        private readonly string _actualTypeString;

        public void InternalSet(object instance, string method, object value)
        {
            if (instance == null) throw new NullReferenceException();
            

            Action<object, object> @delegate;
            if (!_set.TryGetValue(method, out @delegate))
            {
                var instanceParameter = Expression.Parameter(typeof(object), "instance");
                var valueParameter = Expression.Parameter(typeof(object), "value");

                var unboxInstance = Expression.Convert(instanceParameter, _actualType);

                var propertyInfo = _actualType.GetProperty(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var unboxValue = Expression.Convert(valueParameter, propertyInfo.PropertyType);

                var setProperty = Expression.Call(unboxInstance, propertyInfo.SetMethod, unboxValue);

                var lambda = Expression.Lambda<Action<object, object>>(setProperty, instanceParameter, valueParameter);

                _set[method] = @delegate = lambda.Compile();
            }

            @delegate(instance, value);
        }

        public object InternalGet(object instance, string method)
        {
            if (instance == null) throw new NullReferenceException();
                        
            Func<object, object> @delegate;
            if (!_get.TryGetValue(method, out @delegate))
            {
                var parameter = Expression.Parameter(typeof(object), "instance");
                var unbox = Expression.Convert(parameter, _actualType);
                var property = Expression.Property(unbox, method);
                var propertyExpr = Expression.Convert(property, typeof(object));

                var lambda = Expression.Lambda<Func<object, object>>(propertyExpr, parameter);

                _get[method] = @delegate = lambda.Compile();
            }

            return @delegate(instance);
        }
    }
}
