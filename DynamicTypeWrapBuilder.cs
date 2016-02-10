using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Horizon;
using System.Collections.Generic;

namespace Horizon.Forge
{
    static class DynamicTypeWrapBuilder
    {
        const MethodAttributes CtorAttributes = MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName | MethodAttributes.Public;
        const TypeAttributes WrapperClassAttributes = TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed | TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit;
        const FieldAttributes InstaceFieldAttributes = FieldAttributes.Private | FieldAttributes.InitOnly;
        const MethodAttributes GetterSetterAttributes = MethodAttributes.Public | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual;

        static readonly Dictionary<string, Type> _typeCache = new Dictionary<string, Type>();

        internal static dynamic CreateTypeInstance(string typeName, object instance)
        {
            Type type;
            if (!_typeCache.TryGetValue(typeName, out type))
                throw new NotSupportedException("Unknown Horizon Wrapper type.");

            // Now we have our type. Let's create an instance from it:
            var result = Info.Create(type, instance);

            return result;
        }

        //TODO: Refactor to membernodes
        //TODO: Allow multiple interfaces
        //TODO: Check for internal methods / calls on the actual type. The generated IL code will not be able to call them.
        //TODO: Support parent class
        public static Type CreateWrapper(string typeName, Type interfaceWrapperType, Type actualType, bool throwNotSupported)
        {
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

            var actualMethods = Info.Extended.Methods(actualType);

            foreach (var methodCaller in Info.Extended.Methods(interfaceWrapperType))
            {
                MethodInfo method = methodCaller.MethodInfo;

                var parameterTypes = methodCaller.ParameterTypes.Select(x => x.ParameterType).ToArray();

                var methodBuilder = builder.DefineMethod(method.Name, GetterSetterAttributes, method.ReturnType, parameterTypes);

                var methodGenerator = methodBuilder.GetILGenerator();

                methodGenerator.Emit(OpCodes.Ldarg_0);
                methodGenerator.Emit(OpCodes.Ldfld, instanceField);

                var actualMethod = Info.Extended.ResolveSpecificCaller(actualMethods, parameterTypes);

                if (actualMethod != null)
                {
                    for (int i = 1; i <= parameterTypes.Length; i++)
                        methodGenerator.Emit(OpCodes.Ldarg_S, i);

                    methodGenerator.Emit(OpCodes.Call, actualMethod.MethodInfo);
                    methodGenerator.Emit(OpCodes.Ret);
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
                        getIL.Emit(OpCodes.Ldarg_0);
                        getIL.Emit(OpCodes.Ldfld, instanceField);
                        getIL.Emit(OpCodes.Callvirt, actualProperty.GetMethod);
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
                        setIL.Emit(OpCodes.Ldarg_0);
                        setIL.Emit(OpCodes.Ldfld, instanceField);
                        setIL.Emit(OpCodes.Ldarg_1);
                        setIL.Emit(OpCodes.Callvirt, actualProperty.SetMethod);
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
}
