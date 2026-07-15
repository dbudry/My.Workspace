using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using MudBlazor;
using System.Net.Http.Json;
using My.Client.Extensions;
using My.Client.Models;
using My.Shared.Constants;
using My.Shared.Dtos.User;
using My.Shared.Rules;

namespace My.Client.Components.Users
{
    public partial class AddUser
    {
        [Parameter]
        public EventCallback<UserModel> OnUserAdded { get; set; }

        private CreateUserDto NewUser { get; set; } = new();

        private IReadOnlyCollection<string> SelectedRoles { get; set; } = Array.Empty<string>();

        [Inject]
        private IHttpClientFactory ClientFactory { get; set; } = null!;

        [Inject]
        private ISnackbar Snackbar { get; set; } = null!;

        private IReadOnlyList<string> AvailableRoles { get; set; } = Constants.Roles.Assignable();

        [CascadingParameter]
        private Task<Microsoft.AspNetCore.Components.Authorization.AuthenticationState> AuthenticationStateTask { get; set; } = null!;

        protected override async Task OnInitializedAsync()
        {
            var authState = await AuthenticationStateTask;
            AvailableRoles = Constants.Roles.AssignableFor(authState.User);
        }

        private async Task HandleValidSubmit()
        {
            NewUser.Roles = SelectedRoles.ToList();

            if (NewUser.Roles.Count == 0)
            {
                Snackbar.Add("At least one role must be selected.", Severity.Warning);
                return;
            }

            var client = ClientFactory.CreateClient(Constants.API.ClientName);

            try
            {
                var responseMessage = await client.PostAsJsonAsync(Constants.API.User.Create, NewUser);

                if (responseMessage.IsSuccessStatusCode)
                {
                    await OnUserAdded.InvokeAsync(new UserModel());
                    NewUser = new CreateUserDto();
                    SelectedRoles = Array.Empty<string>();
                    Snackbar.Add("User created successfully.", Severity.Success);
                }
                else
                {
                    var error = await responseMessage.Content.ReadAsStringAsync();
                    Snackbar.Add(error, Severity.Error);
                }
            }
            catch (Exception ex)
            {
                Snackbar.AddApiError(ex, "Couldn't create user.");
            }
        }

        private void HandleInvalidSubmit(EditContext context)
        {
            var errorMessages = context.GetValidationMessages();
            foreach (var errorMessage in errorMessages)
            {
                Snackbar.Add(errorMessage, Severity.Error);
            }
        }

        /// <summary>
        /// On Email blur, fill blank FirstName/LastName from the email's local-part as a
        /// starting guess so the admin doesn't have to retype names they already typed in
        /// the address. Never overwrites a value the admin has already entered.
        /// The user's heal-on-sign-in path will upgrade the guess to their real Google name
        /// the first time they log in.
        /// </summary>
        private void AutoFillNameFromEmail()
        {
            if (string.IsNullOrWhiteSpace(NewUser.Email)) return;
            var (first, last) = UserNameRules.ParseFromEmail(NewUser.Email);
            if (string.IsNullOrWhiteSpace(NewUser.FirstName) && !string.IsNullOrEmpty(first))
                NewUser.FirstName = first;
            if (string.IsNullOrWhiteSpace(NewUser.LastName) && !string.IsNullOrEmpty(last))
                NewUser.LastName = last;
        }
    }
}
