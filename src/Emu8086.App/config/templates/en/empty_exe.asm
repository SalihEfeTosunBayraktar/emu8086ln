; Empty EXE program
#make_EXE#

data segment
    ; Put your variables here
ends

stack segment
    dw 128 dup(0)
ends

code segment
start:
    mov ax, data
    mov ds, ax
    mov es, ax

    ; Write your code here

    mov ax, 4C00h           ; Terminate the program
    int 21h
ends

end start
