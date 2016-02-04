using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Horizon;

namespace Horizon.Forge
{
    class EmptyMethodMemberDefinition : MemberDefinition<MethodBuilder>
    {
        private const MethodAttributes _defaultMethodAttributes = MethodAttributes.Public | MethodAttributes.Final |
                                                                  MethodAttributes.HideBySig | MethodAttributes.NewSlot |
                                                                  MethodAttributes.Virtual;

        public Type[] ParameterTypes { get; private set; }

        public EmptyMethodMemberDefinition(IMethodCaller caller, bool isVirtual)
            : base(caller.Name, caller.ReturnType, isVirtual)
        {
            ParameterTypes = caller.ParameterTypes.Select(x => x.ParameterType).ToArray();
        }

        public EmptyMethodMemberDefinition(string name, Type type, bool isVirtual, Type[] parameterTypes)
            : base(name, type, isVirtual)
        {
            ParameterTypes = parameterTypes;
        }

        public EmptyMethodMemberDefinition(string name, Type type, bool isVirtual)
            : base(name, type, isVirtual)
        {
            ParameterTypes = Type.EmptyTypes;
        }

        internal override MethodBuilder Build(System.Reflection.Emit.TypeBuilder typeBuilder)
        {
            var name = Name;
            var returnType = MemberType;
            var parameterTypes = ParameterTypes;

            MethodBuilder methodBuilder = typeBuilder.DefineMethod(name, _defaultMethodAttributes, returnType, parameterTypes);
            
            Build(returnType, methodBuilder.GetILGenerator());
            
            return methodBuilder;
        }

        internal static void Build(Type returnType, ILGenerator generator)
        {
            if (returnType != typeof(void))
            {
                if (!returnType.IsValueType)
                {
                    generator.Emit(OpCodes.Ldnull);
                }
                else if (returnType.IsPrimitive)
                {
                    generator.Emit(OpCodes.Ldc_I4_0);

                    //Do we need to manually convert? Seems to work on its own!
                    //var size = System.Runtime.InteropServices.Marshal.SizeOf(returnType);
                    //if (size == 8)
                    //    generator.Emit(OpCodes.Conv_I8);
                }
                else
                {
                    var local = generator.DeclareLocal(returnType);
                    generator.Emit(OpCodes.Ldloca_S, local);
                    generator.Emit(OpCodes.Initobj, returnType);
                    generator.Emit(OpCodes.Ldloc_0);
                }
            }

            generator.Emit(OpCodes.Ret);
        }
    }
}