using System;
using System.Reflection;
using System.Reflection.Emit;

namespace Horizon.Forge
{
    class PropertyMemberDefinition : MemberDefinition<PropertyBuilder>
    {
        public MethodInfo RaisePropertyChanged { get; set; }

        public PropertyMemberDefinition(string name, Type type, bool isVirtual, MethodInfo raisePropertyChanged)
            : base(name, type, isVirtual)
        {
            RaisePropertyChanged = raisePropertyChanged;
        }

        internal override PropertyBuilder Build(System.Reflection.Emit.TypeBuilder typeBuilder)
        {
            var memberName = Name;
            var memberType = MemberType;

            var field = typeBuilder.DefineField("m_" + memberName, memberType, FieldAttributes.Private);

            var methodAttributes = IsVirtual ? VirtPublicProperty : PublicProperty;

            // Define the property getter method for our private field.
            var getBuilder = typeBuilder.DefineMethod("get_" + memberName, methodAttributes, memberType, Type.EmptyTypes);

            var getIL = getBuilder.GetILGenerator();
            getIL.Emit(OpCodes.Ldarg_0);
            getIL.Emit(OpCodes.Ldfld, field);
            getIL.Emit(OpCodes.Ret);

            var parameterTypes = new[] {memberType};

            // Define the property setter method for our private field.
            var setBuilder = typeBuilder.DefineMethod("set_" + memberName, methodAttributes, typeof(void), parameterTypes);

            var setIL = setBuilder.GetILGenerator();

            setIL.Emit(OpCodes.Ldarg_0);

            var notifyChanges = RaisePropertyChanged != null;

            if (notifyChanges)
            {
                setIL.Emit(OpCodes.Ldfld, field);
                setIL.Emit(OpCodes.Ldarg_1);

                var setFieldLabel = setIL.DefineLabel();
                setIL.Emit(OpCodes.Bne_Un_S, setFieldLabel);

                setIL.Emit(OpCodes.Ret);


                setIL.MarkLabel(setFieldLabel);
                setIL.Emit(OpCodes.Ldarg_0);
            }

            setIL.Emit(OpCodes.Ldarg_1);
            setIL.Emit(OpCodes.Stfld, field);

            if (notifyChanges)
            {
                setIL.Emit(OpCodes.Ldarg_0);
                setIL.Emit(OpCodes.Ldstr, memberName);
                setIL.Emit(OpCodes.Call, RaisePropertyChanged);
            }

            setIL.Emit(OpCodes.Ret);

            // Generate a public property
            var property = typeBuilder.DefineProperty(memberName, PropertyAttributes.None, memberType, parameterTypes);

            // Map our two methods created above to their corresponding behaviors, "get" and "set" respectively. 
            property.SetGetMethod(getBuilder);
            property.SetSetMethod(setBuilder);

            return property;
        }
    }
}